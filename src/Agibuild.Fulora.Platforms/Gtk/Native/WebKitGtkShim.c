/*
 * Native WebKitGTK shim
 * Exposes a stable C ABI for use from net10.0 via P/Invoke.
 *
 * Build requirements:
 * - gcc/clang with pkg-config
 * - libwebkit2gtk-4.1-dev (or webkit2gtk-4.1 devel package)
 *
 * Build command:
 *   gcc -shared -fPIC -o libAgibuildWebViewGtk.so WebKitGtkShim.c \
 *       $(pkg-config --cflags --libs webkit2gtk-4.1 gtk+-3.0)
 */

#include <gtk/gtk.h>
#include <gtk/gtkx.h>
#include <webkit2/webkit2.h>
#include <string.h>
#include <stdlib.h>
#include <stdint.h>
#include <stdbool.h>
#include <stdatomic.h>

/* ========== Callback typedefs ========== */

typedef void (*ag_gtk_policy_request_cb)(
    void* user_data,
    uint64_t request_id,
    const char* url_utf8,
    bool is_main_frame,
    bool is_new_window,
    int navigation_type);

typedef void (*ag_gtk_nav_completed_cb)(
    void* user_data,
    const char* url_utf8,
    int status, /* 0=Success, 1=Failure, 2=Canceled, 3=Timeout, 4=Network, 5=Ssl */
    int64_t error_code,
    const char* error_message_utf8);

typedef void (*ag_gtk_script_result_cb)(
    void* user_data,
    uint64_t request_id,
    const char* result_utf8,
    const char* error_message_utf8);

typedef void (*ag_gtk_message_cb)(
    void* user_data,
    const char* body_utf8,
    const char* origin_utf8);

typedef void (*ag_gtk_download_cb)(
    void* user_data,
    const char* url_utf8,
    const char* suggested_filename_utf8,
    const char* mime_type_utf8,
    int64_t content_length);

typedef void (*ag_gtk_permission_cb)(
    void* user_data,
    int permission_kind, /* 0=Unknown, 1=Camera, 2=Microphone, 3=Geolocation, 6=Notifications */
    const char* origin_utf8,
    int* out_state); /* 0=Default, 1=Allow, 2=Deny */

typedef bool (*ag_gtk_scheme_request_cb)(
    void* user_data,
    const char* url_utf8,
    const char* method_utf8,
    const void** out_response_data,
    int64_t* out_response_length,
    const char** out_mime_type_utf8,
    int* out_status_code);

typedef bool (*ag_gtk_context_menu_cb)(
    void* user_data,
    double x, double y,
    const char* link_uri,
    const char* selection_text,
    int media_type, /* 0=None, 1=Image, 2=Video, 3=Audio */
    const char* media_source_uri,
    bool is_editable);

typedef void (*ag_gtk_drag_entered_cb)(
    void* user_data,
    const char* files_json_utf8,
    const char* text_utf8,
    double x, double y);

typedef void (*ag_gtk_drag_updated_cb)(
    void* user_data,
    double x, double y);

typedef void (*ag_gtk_drag_exited_cb)(
    void* user_data);

typedef void (*ag_gtk_drop_performed_cb)(
    void* user_data,
    const char* files_json_utf8,
    const char* text_utf8,
    double x, double y);

struct ag_gtk_callbacks
{
    ag_gtk_policy_request_cb on_policy_request;
    ag_gtk_nav_completed_cb on_navigation_completed;
    ag_gtk_script_result_cb on_script_result;
    ag_gtk_message_cb on_message;
    ag_gtk_download_cb on_download;
    ag_gtk_permission_cb on_permission;
    ag_gtk_scheme_request_cb on_scheme_request;
    ag_gtk_context_menu_cb on_context_menu;
    ag_gtk_drag_entered_cb on_drag_entered;
    ag_gtk_drag_updated_cb on_drag_updated;
    ag_gtk_drag_exited_cb on_drag_exited;
    ag_gtk_drop_performed_cb on_drop_performed;
};

/* ========== Cookie operation callbacks ========== */

typedef void (*ag_gtk_cookies_get_cb)(void* context, const char* json_utf8);
typedef void (*ag_gtk_cookie_op_cb)(void* context, bool success, const char* error_utf8);

/* ========== Shim state ========== */

typedef struct
{
    struct ag_gtk_callbacks callbacks;
    void* user_data;

    GtkWidget* plug;         /* GtkPlug embedding container */
    WebKitWebView* web_view;
    WebKitUserContentManager* content_manager;
    WebKitWebsiteDataManager* data_manager;

    atomic_uint_fast64_t next_request_id;
    atomic_bool detached;
    atomic_bool dev_tools_open;
    gboolean gtk_initialized;

    /* Pending policy decisions: request_id -> WebKitPolicyDecision* */
    GHashTable* pending_policy;

    /* Options — set before attach. */
    gboolean opt_enable_dev_tools;
    gboolean opt_ephemeral;
    char* opt_user_agent; /* owned, NULL if not set */

    /* Custom scheme registrations — set before attach. */
    char** custom_schemes; /* NULL-terminated array of scheme strings, owned */
    int custom_scheme_count;

    /* Drag-drop state — whether drag is currently over the widget */
    gboolean drag_inside;

} shim_state;

typedef void* ag_gtk_handle;

/* Forward declarations */
void ag_gtk_detach(ag_gtk_handle handle);

/* ========== GTK thread safety ========== */

static void ensure_gtk_init(void)
{
    static gboolean initialized = FALSE;
    if (!initialized)
    {
        if (!gtk_init_check(NULL, NULL))
        {
            /* GTK could not initialize — probably no display. */
            return;
        }
        initialized = TRUE;
    }
}

typedef struct
{
    void (*func)(void* data);
    void* data;
    gboolean done;
    GMutex mutex;
    GCond cond;
} sync_call;

static gboolean sync_call_idle(gpointer user_data)
{
    sync_call* sc = (sync_call*)user_data;
    sc->func(sc->data);
    g_mutex_lock(&sc->mutex);
    sc->done = TRUE;
    g_cond_signal(&sc->cond);
    g_mutex_unlock(&sc->mutex);
    return G_SOURCE_REMOVE;
}

/* Run a function on the GTK main thread synchronously. */
static void run_on_gtk_thread(void (*func)(void*), void* data)
{
    /* If we're on the main context's thread already, just call directly. */
    if (g_main_context_is_owner(g_main_context_default()))
    {
        func(data);
        return;
    }

    sync_call sc;
    sc.func = func;
    sc.data = data;
    sc.done = FALSE;
    g_mutex_init(&sc.mutex);
    g_cond_init(&sc.cond);

    g_idle_add(sync_call_idle, &sc);

    g_mutex_lock(&sc.mutex);
    while (!sc.done)
        g_cond_wait(&sc.cond, &sc.mutex);
    g_mutex_unlock(&sc.mutex);

    g_mutex_clear(&sc.mutex);
    g_cond_clear(&sc.cond);
}

/* ========== Error status mapping ========== */

/* Map WebKitGTK error codes to our status codes:
 * 0=Success, 1=Failure, 2=Canceled, 3=Timeout, 4=Network, 5=Ssl */
static int map_webkit_error(GError* error)
{
    if (error == NULL)
        return 1;

    if (g_error_matches(error, WEBKIT_NETWORK_ERROR, WEBKIT_NETWORK_ERROR_CANCELLED))
        return 2;

    if (g_error_matches(error, WEBKIT_NETWORK_ERROR, WEBKIT_NETWORK_ERROR_TRANSPORT))
        return 4;

    if (g_error_matches(error, WEBKIT_NETWORK_ERROR, WEBKIT_NETWORK_ERROR_UNKNOWN_PROTOCOL) ||
        g_error_matches(error, WEBKIT_NETWORK_ERROR, WEBKIT_NETWORK_ERROR_FAILED))
        return 4;

    if (error->domain == WEBKIT_POLICY_ERROR)
        return 2; /* Policy errors are typically cancellations */

    /* TLS/SSL errors */
    if (g_error_matches(error, G_TLS_ERROR, G_TLS_ERROR_BAD_CERTIFICATE) ||
        g_error_matches(error, G_TLS_ERROR, G_TLS_ERROR_NOT_TLS) ||
        g_error_matches(error, G_TLS_ERROR, G_TLS_ERROR_CERTIFICATE_REQUIRED))
        return 5;

    return 1; /* General failure */
}

/* ========== WebKitGTK signal handlers ========== */

static gboolean on_decide_policy(WebKitWebView* web_view, WebKitPolicyDecision* decision,
                                  WebKitPolicyDecisionType type, gpointer user_data)
{
    shim_state* s = (shim_state*)user_data;
    if (atomic_load(&s->detached))
    {
        webkit_policy_decision_ignore(decision);
        return TRUE;
    }

    if (type == WEBKIT_POLICY_DECISION_TYPE_NEW_WINDOW_ACTION)
    {
        WebKitNavigationPolicyDecision* nav_decision = WEBKIT_NAVIGATION_POLICY_DECISION(decision);
        WebKitNavigationAction* action = webkit_navigation_policy_decision_get_navigation_action(nav_decision);
        WebKitURIRequest* request = webkit_navigation_action_get_request(action);
        const char* url = webkit_uri_request_get_uri(request);

        if (s->callbacks.on_policy_request)
        {
            uint64_t req_id = atomic_fetch_add(&s->next_request_id, 1);
            g_object_ref(decision);
            g_hash_table_insert(s->pending_policy, GUINT_TO_POINTER((guint)req_id), decision);
            s->callbacks.on_policy_request(s->user_data, req_id, url ? url : "", FALSE, TRUE, 0);
        }
        else
        {
            webkit_policy_decision_ignore(decision);
        }
        return TRUE;
    }

    if (type == WEBKIT_POLICY_DECISION_TYPE_NAVIGATION_ACTION)
    {
        WebKitNavigationPolicyDecision* nav_decision = WEBKIT_NAVIGATION_POLICY_DECISION(decision);
        WebKitNavigationAction* action = webkit_navigation_policy_decision_get_navigation_action(nav_decision);
        WebKitURIRequest* request = webkit_navigation_action_get_request(action);
        const char* url = webkit_uri_request_get_uri(request);
        int nav_type = (int)webkit_navigation_action_get_navigation_type(action);
        gboolean is_main = webkit_navigation_policy_decision_get_frame_name(nav_decision) == NULL;

        if (s->callbacks.on_policy_request)
        {
            uint64_t req_id = atomic_fetch_add(&s->next_request_id, 1);
            g_object_ref(decision);
            g_hash_table_insert(s->pending_policy, GUINT_TO_POINTER((guint)req_id), decision);
            s->callbacks.on_policy_request(s->user_data, req_id, url ? url : "", is_main, FALSE, nav_type);
        }
        else
        {
            webkit_policy_decision_use(decision);
        }
        return TRUE;
    }

    return FALSE; /* Let WebKit handle other decision types */
}

static void on_load_changed(WebKitWebView* web_view, WebKitLoadEvent event, gpointer user_data)
{
    shim_state* s = (shim_state*)user_data;
    if (atomic_load(&s->detached))
        return;

    if (event == WEBKIT_LOAD_FINISHED)
    {
        if (s->callbacks.on_navigation_completed)
        {
            const char* url = webkit_web_view_get_uri(web_view);
            s->callbacks.on_navigation_completed(s->user_data, url ? url : "about:blank", 0, 0, "");
        }
    }
}

static gboolean on_load_failed(WebKitWebView* web_view, WebKitLoadEvent event,
                                const char* failing_uri, GError* error, gpointer user_data)
{
    shim_state* s = (shim_state*)user_data;
    if (atomic_load(&s->detached))
        return TRUE;

    if (s->callbacks.on_navigation_completed)
    {
        int status = map_webkit_error(error);
        int64_t code = error ? (int64_t)error->code : 0;
        const char* msg = error ? error->message : "Unknown error";
        s->callbacks.on_navigation_completed(s->user_data, failing_uri ? failing_uri : "about:blank", status, code, msg);
    }

    return TRUE; /* We handled it */
}

static gboolean on_load_failed_tls(WebKitWebView* web_view, const char* failing_uri,
                                    GTlsCertificate* certificate, GTlsCertificateFlags errors,
                                    gpointer user_data)
{
    shim_state* s = (shim_state*)user_data;
    if (atomic_load(&s->detached))
        return TRUE;

    if (s->callbacks.on_navigation_completed)
    {
        s->callbacks.on_navigation_completed(s->user_data,
            failing_uri ? failing_uri : "about:blank",
            5 /* SSL */, (int64_t)errors, "TLS certificate error");
    }

    return TRUE;
}

static void on_script_message(WebKitUserContentManager* manager, WebKitJavascriptResult* result,
                               gpointer user_data)
{
    shim_state* s = (shim_state*)user_data;
    if (atomic_load(&s->detached))
        return;

    if (s->callbacks.on_message)
    {
        JSCValue* value = webkit_javascript_result_get_js_value(result);
        char* body = jsc_value_to_string(value);
        const char* url = webkit_web_view_get_uri(s->web_view);

        /* Extract origin from current URI */
        char origin[512] = "";
        if (url)
        {
            /* Simple origin extraction: scheme://host[:port] */
            const char* scheme_end = strstr(url, "://");
            if (scheme_end)
            {
                const char* host_start = scheme_end + 3;
                const char* path_start = strchr(host_start, '/');
                size_t origin_len = path_start ? (size_t)(path_start - url) : strlen(url);
                if (origin_len < sizeof(origin))
                {
                    memcpy(origin, url, origin_len);
                    origin[origin_len] = '\0';
                }
            }
        }

        s->callbacks.on_message(s->user_data, body ? body : "", origin);
        g_free(body);
    }
}

/* ========== Script evaluation callback data ========== */

typedef struct
{
    shim_state* state;
    uint64_t request_id;
} eval_js_data;

static void on_eval_js_finish(GObject* source, GAsyncResult* result, gpointer user_data)
{
    eval_js_data* data = (eval_js_data*)user_data;
    shim_state* s = data->state;
    uint64_t req_id = data->request_id;
    free(data);

    if (atomic_load(&s->detached))
        return;

    if (!s->callbacks.on_script_result)
        return;

    GError* error = NULL;
    WebKitJavascriptResult* js_result = webkit_web_view_run_javascript_finish(
        WEBKIT_WEB_VIEW(source), result, &error);

    if (error != NULL)
    {
        s->callbacks.on_script_result(s->user_data, req_id, NULL, error->message);
        g_error_free(error);
        return;
    }

    if (js_result == NULL)
    {
        s->callbacks.on_script_result(s->user_data, req_id, NULL, NULL);
        return;
    }

    JSCValue* value = webkit_javascript_result_get_js_value(js_result);
    if (jsc_value_is_undefined(value) || jsc_value_is_null(value))
    {
        s->callbacks.on_script_result(s->user_data, req_id, NULL, NULL);
        webkit_javascript_result_unref(js_result);
        return;
    }

    char* str = jsc_value_to_string(value);
    s->callbacks.on_script_result(s->user_data, req_id, str ? str : NULL, NULL);
    g_free(str);
    webkit_javascript_result_unref(js_result);
}

/* ========== Custom scheme handler ========== */

static void on_custom_scheme_request(WebKitURISchemeRequest* request, gpointer user_data)
{
    shim_state* s = (shim_state*)user_data;
    if (atomic_load(&s->detached) || s->callbacks.on_scheme_request == NULL)
    {
        GError* err = g_error_new_literal(g_quark_from_string("ag-webkit"), 404, "Not handled");
        webkit_uri_scheme_request_finish_error(request, err);
        g_error_free(err);
        return;
    }

    const char* uri = webkit_uri_scheme_request_get_uri(request);
    const char* method = webkit_uri_scheme_request_get_http_method(request);

    const void* response_data = NULL;
    int64_t response_length = 0;
    const char* mime_type = NULL;
    int status_code = 0;

    bool handled = s->callbacks.on_scheme_request(
        s->user_data,
        uri ? uri : "",
        method ? method : "GET",
        &response_data, &response_length, &mime_type, &status_code);

    if (!handled || response_data == NULL)
    {
        GError* err = g_error_new_literal(g_quark_from_string("ag-webkit"), 404, "Not handled");
        webkit_uri_scheme_request_finish_error(request, err);
        g_error_free(err);
        return;
    }

    GInputStream* stream = g_memory_input_stream_new_from_data(
        g_memdup2(response_data, (gsize)response_length),
        (gssize)response_length,
        g_free);

    webkit_uri_scheme_request_finish(request, stream, (gssize)response_length,
        mime_type ? mime_type : "application/octet-stream");

    g_object_unref(stream);
}

/* ========== Download signal handler ========== */

static void on_download_started(WebKitWebContext* context, WebKitDownload* download, gpointer user_data)
{
    shim_state* s = (shim_state*)user_data;
    if (atomic_load(&s->detached)) return;
    if (s->callbacks.on_download == NULL) return;

    WebKitURIRequest* request = webkit_download_get_request(download);
    const char* url = request ? webkit_uri_request_get_uri(request) : "";

    WebKitURIResponse* response = webkit_download_get_response(download);
    const char* mime = response ? webkit_uri_response_get_mime_type(response) : "";
    int64_t length = response ? (int64_t)webkit_uri_response_get_content_length(response) : -1;

    const char* suggested = webkit_download_get_suggested_filename(download);
    if (suggested == NULL) suggested = "";

    s->callbacks.on_download(s->user_data, url, suggested, mime ? mime : "", length > 0 ? length : -1);
}

/* ========== Permission signal handler ========== */

static gboolean on_permission_request(WebKitWebView* web_view, WebKitPermissionRequest* request, gpointer user_data)
{
    shim_state* s = (shim_state*)user_data;
    if (atomic_load(&s->detached)) return FALSE;
    if (s->callbacks.on_permission == NULL) return FALSE;

    int kind = 0; /* Unknown */
    if (WEBKIT_IS_MEDIA_KEY_SYSTEM_PERMISSION_REQUEST(request))
    {
        kind = 0;
    }
    else
    {
        /* Attempt to identify by type name since WebKitGTK doesn't
           always expose all request types in older versions. */
        const gchar* type_name = G_OBJECT_TYPE_NAME(request);
        if (type_name != NULL)
        {
            if (g_str_has_prefix(type_name, "WebKitGeolocation"))
                kind = 3; /* Geolocation */
            else if (g_str_has_prefix(type_name, "WebKitNotification"))
                kind = 6; /* Notifications */
            else if (g_str_has_prefix(type_name, "WebKitUserMedia"))
                kind = 1; /* Camera (media capture) */
        }
    }

    /* Get origin from the main resource URI */
    const char* uri = webkit_web_view_get_uri(web_view);
    int state = 0; /* Default */
    s->callbacks.on_permission(s->user_data, kind, uri ? uri : "", &state);

    if (state == 1) /* Allow */
    {
        webkit_permission_request_allow(request);
        return TRUE;
    }
    else if (state == 2) /* Deny */
    {
        webkit_permission_request_deny(request);
        return TRUE;
    }

    return FALSE; /* Let WebKitGTK handle default behavior */
}

/* ========== Context menu signal handler ========== */

static gboolean on_context_menu(WebKitWebView* web_view, WebKitContextMenu* context_menu,
                                GdkEvent* event, WebKitHitTestResult* hit_test_result,
                                gpointer user_data)
{
    shim_state* s = (shim_state*)user_data;
    if (atomic_load(&s->detached)) return FALSE;
    if (s->callbacks.on_context_menu == NULL) return FALSE;

    double x = 0, y = 0;
    if (event != NULL && event->type == GDK_BUTTON_PRESS)
    {
        x = ((GdkEventButton*)event)->x;
        y = ((GdkEventButton*)event)->y;
    }

    const char* link_uri = NULL;
    const char* media_uri = NULL;
    bool is_editable = false;
    int media_type = 0;

    if (hit_test_result != NULL)
    {
        if (webkit_hit_test_result_context_is_link(hit_test_result))
            link_uri = webkit_hit_test_result_get_link_uri(hit_test_result);

        is_editable = webkit_hit_test_result_context_is_editable(hit_test_result);

        if (webkit_hit_test_result_context_is_image(hit_test_result))
        {
            media_type = 1;
            media_uri = webkit_hit_test_result_get_image_uri(hit_test_result);
        }
        else if (webkit_hit_test_result_context_is_media(hit_test_result))
        {
            media_type = 2; /* Video — WebKitGTK doesn't distinguish video/audio */
            media_uri = webkit_hit_test_result_get_media_uri(hit_test_result);
        }
    }

    /* Selection text requires evaluating JS; WebKitGTK doesn't expose it via hit-test.
       Pass NULL and let the managed side handle it if needed. */
    bool handled = s->callbacks.on_context_menu(
        s->user_data, x, y, link_uri, NULL, media_type, media_uri, is_editable);

    return handled ? TRUE : FALSE;
}

/* ========== Drag-drop signal handlers ========== */

static gboolean on_drag_motion(GtkWidget* widget, GdkDragContext* context,
    gint x, gint y, guint time, gpointer user_data)
{
    (void)widget;
    (void)time;
    shim_state* s = (shim_state*)user_data;
    if (atomic_load(&s->detached)) return FALSE;

    if (!s->drag_inside)
    {
        s->drag_inside = TRUE;
        if (s->callbacks.on_drag_entered)
            s->callbacks.on_drag_entered(s->user_data, "[]", NULL, (double)x, (double)y);
    }
    else if (s->callbacks.on_drag_updated)
    {
        s->callbacks.on_drag_updated(s->user_data, (double)x, (double)y);
    }

    gdk_drag_status(context, GDK_ACTION_COPY, time);
    return TRUE;
}

static void on_drag_leave(GtkWidget* widget, GdkDragContext* context,
    guint time, gpointer user_data)
{
    (void)widget;
    (void)context;
    (void)time;
    shim_state* s = (shim_state*)user_data;
    if (atomic_load(&s->detached)) return;

    s->drag_inside = FALSE;
    if (s->callbacks.on_drag_exited)
        s->callbacks.on_drag_exited(s->user_data);
}

static void on_drag_data_received(GtkWidget* widget, GdkDragContext* context,
    gint x, gint y, GtkSelectionData* data, guint info, guint time, gpointer user_data)
{
    (void)widget;
    shim_state* s = (shim_state*)user_data;
    if (atomic_load(&s->detached))
    {
        gtk_drag_finish(context, FALSE, FALSE, time);
        return;
    }

    s->drag_inside = FALSE;

    const char* text = NULL;
    const char* files_json = "[]";
    char* files_buf = NULL;

    if (info == 0) /* text/uri-list */
    {
        gchar** uris = gtk_selection_data_get_uris(data);
        if (uris)
        {
            size_t buf_size = 1024;
            files_buf = (char*)malloc(buf_size);
            strcpy(files_buf, "[");

            for (int i = 0; uris[i]; i++)
            {
                gchar* path = g_filename_from_uri(uris[i], NULL, NULL);
                if (path)
                {
                    char entry[512];
                    snprintf(entry, sizeof(entry),
                        "%s{\"path\":\"%s\"}", i > 0 ? "," : "", path);
                    if (strlen(files_buf) + strlen(entry) + 2 > buf_size)
                    {
                        buf_size *= 2;
                        files_buf = (char*)realloc(files_buf, buf_size);
                    }
                    strcat(files_buf, entry);
                    g_free(path);
                }
            }
            strcat(files_buf, "]");
            files_json = files_buf;
            g_strfreev(uris);
        }
    }
    else /* text/plain or UTF8_STRING */
    {
        text = (const char*)gtk_selection_data_get_data(data);
    }

    if (s->callbacks.on_drop_performed)
        s->callbacks.on_drop_performed(s->user_data, files_json, text, (double)x, (double)y);

    if (files_buf)
        free(files_buf);
    gtk_drag_finish(context, TRUE, FALSE, time);
}

/* ========== Attach helper ========== */

typedef struct
{
    shim_state* state;
    gulong x11_window_id;
    gboolean result;
} attach_data;

static void do_attach(void* data)
{
    attach_data* ad = (attach_data*)data;
    shim_state* s = ad->state;

    if (atomic_load(&s->detached))
    {
        ad->result = FALSE;
        return;
    }

    /* Create a GtkPlug to embed into the X11 window provided by Avalonia NativeControlHost */
    s->plug = gtk_plug_new((Window)ad->x11_window_id);
    if (s->plug == NULL)
    {
        ad->result = FALSE;
        return;
    }

    /* Create content manager for script message handling */
    s->content_manager = webkit_user_content_manager_new();
    g_signal_connect(s->content_manager, "script-message-received::agibuildWebView",
                     G_CALLBACK(on_script_message), s);
    webkit_user_content_manager_register_script_message_handler(s->content_manager, "agibuildWebView");

    /* Create WebKitWebView */
    if (s->opt_ephemeral)
    {
        WebKitWebContext* ctx = webkit_web_context_new_ephemeral();
        s->web_view = WEBKIT_WEB_VIEW(g_object_new(WEBKIT_TYPE_WEB_VIEW,
            "web-context", ctx,
            "user-content-manager", s->content_manager,
            NULL));
        g_object_unref(ctx);
    }
    else
    {
        s->web_view = WEBKIT_WEB_VIEW(g_object_new(WEBKIT_TYPE_WEB_VIEW,
            "user-content-manager", s->content_manager,
            NULL));
    }

    /* Apply DevTools setting */
    WebKitSettings* settings = webkit_web_view_get_settings(s->web_view);
    webkit_settings_set_enable_developer_extras(settings, s->opt_enable_dev_tools);

    /* Apply custom user agent */
    if (s->opt_user_agent != NULL)
    {
        webkit_settings_set_user_agent(settings, s->opt_user_agent);
    }

    /* Enable JavaScript */
    webkit_settings_set_enable_javascript(settings, TRUE);

    /* Connect signals */
    g_signal_connect(s->web_view, "decide-policy", G_CALLBACK(on_decide_policy), s);
    g_signal_connect(s->web_view, "load-changed", G_CALLBACK(on_load_changed), s);
    g_signal_connect(s->web_view, "load-failed", G_CALLBACK(on_load_failed), s);
    g_signal_connect(s->web_view, "load-failed-with-tls-errors", G_CALLBACK(on_load_failed_tls), s);

    /* Register custom URI schemes */
    WebKitWebContext* web_context = webkit_web_view_get_context(s->web_view);
    if (s->custom_schemes != NULL && s->callbacks.on_scheme_request != NULL)
    {
        for (int i = 0; i < s->custom_scheme_count; i++)
        {
            webkit_web_context_register_uri_scheme(web_context, s->custom_schemes[i],
                on_custom_scheme_request, s, NULL);
        }
    }

    /* Download signal */
    g_signal_connect(web_context, "download-started", G_CALLBACK(on_download_started), s);

    /* Permission signal */
    g_signal_connect(s->web_view, "permission-request", G_CALLBACK(on_permission_request), s);

    /* Context menu signal */
    if (s->callbacks.on_context_menu != NULL)
    {
        g_signal_connect(s->web_view, "context-menu", G_CALLBACK(on_context_menu), s);
    }

    /* Set up drag-and-drop targets */
    GtkTargetEntry targets[] = {
        {"text/uri-list", 0, 0},
        {"text/plain", 0, 1},
        {"UTF8_STRING", 0, 2}
    };
    gtk_drag_dest_set(GTK_WIDGET(s->web_view),
        GTK_DEST_DEFAULT_ALL,
        targets, G_N_ELEMENTS(targets),
        GDK_ACTION_COPY);
    g_signal_connect(s->web_view, "drag-data-received", G_CALLBACK(on_drag_data_received), s);
    g_signal_connect(s->web_view, "drag-motion", G_CALLBACK(on_drag_motion), s);
    g_signal_connect(s->web_view, "drag-leave", G_CALLBACK(on_drag_leave), s);

    /* Add WebView to the plug */
    gtk_container_add(GTK_CONTAINER(s->plug), GTK_WIDGET(s->web_view));
    gtk_widget_show_all(s->plug);

    ad->result = TRUE;
}

/* ========== Detach helper ========== */

static void do_detach(void* data)
{
    shim_state* s = (shim_state*)data;
    gboolean was_detached = atomic_exchange(&s->detached, TRUE);
    if (was_detached)
        return;

    atomic_store(&s->dev_tools_open, FALSE);

    /* Unregister script message handler */
    if (s->content_manager != NULL)
    {
        webkit_user_content_manager_unregister_script_message_handler(s->content_manager, "agibuildWebView");
    }

    /* Destroy the plug (and its children including web_view) */
    if (s->plug != NULL)
    {
        gtk_widget_destroy(s->plug);
        s->plug = NULL;
    }

    /* Cancel all pending policy decisions */
    if (s->pending_policy != NULL)
    {
        GHashTableIter iter;
        gpointer key, value;
        g_hash_table_iter_init(&iter, s->pending_policy);
        while (g_hash_table_iter_next(&iter, &key, &value))
        {
            WebKitPolicyDecision* decision = (WebKitPolicyDecision*)value;
            webkit_policy_decision_ignore(decision);
            g_object_unref(decision);
        }
        g_hash_table_remove_all(s->pending_policy);
    }

    s->web_view = NULL;
    s->content_manager = NULL;
}

/* ========== Public API ========== */

ag_gtk_handle ag_gtk_create(const struct ag_gtk_callbacks* callbacks, void* user_data)
{
    ensure_gtk_init();

    shim_state* s = (shim_state*)calloc(1, sizeof(shim_state));
    if (s == NULL)
        return NULL;

    if (callbacks)
    {
        s->callbacks = *callbacks;
    }
    s->user_data = user_data;
    atomic_init(&s->next_request_id, 1);
    atomic_init(&s->detached, FALSE);
    atomic_init(&s->dev_tools_open, FALSE);
    s->pending_policy = g_hash_table_new(g_direct_hash, g_direct_equal);

    return (ag_gtk_handle)s;
}

void ag_gtk_register_custom_scheme(ag_gtk_handle handle, const char* scheme_utf8)
{
    if (!handle || !scheme_utf8) return;
    shim_state* s = (shim_state*)handle;

    int new_count = s->custom_scheme_count + 1;
    s->custom_schemes = (char**)realloc(s->custom_schemes, sizeof(char*) * (new_count + 1));
    s->custom_schemes[s->custom_scheme_count] = strdup(scheme_utf8);
    s->custom_schemes[new_count] = NULL; /* NULL-terminate */
    s->custom_scheme_count = new_count;
}

void ag_gtk_destroy(ag_gtk_handle handle)
{
    if (!handle) return;
    shim_state* s = (shim_state*)handle;
    ag_gtk_detach(handle);

    if (s->pending_policy != NULL)
    {
        g_hash_table_destroy(s->pending_policy);
        s->pending_policy = NULL;
    }

    /* Free custom schemes */
    if (s->custom_schemes != NULL)
    {
        for (int i = 0; i < s->custom_scheme_count; i++)
        {
            free(s->custom_schemes[i]);
        }
        free(s->custom_schemes);
    }

    free(s->opt_user_agent);
    free(s);
}

bool ag_gtk_attach(ag_gtk_handle handle, unsigned long x11_window_id)
{
    if (!handle || x11_window_id == 0) return false;
    shim_state* s = (shim_state*)handle;

    attach_data ad;
    ad.state = s;
    ad.x11_window_id = x11_window_id;
    ad.result = FALSE;

    run_on_gtk_thread(do_attach, &ad);
    return ad.result;
}

void ag_gtk_detach(ag_gtk_handle handle)
{
    if (!handle) return;
    shim_state* s = (shim_state*)handle;
    run_on_gtk_thread(do_detach, s);
}

void ag_gtk_policy_decide(ag_gtk_handle handle, uint64_t request_id, bool allow)
{
    if (!handle || request_id == 0) return;
    shim_state* s = (shim_state*)handle;

    WebKitPolicyDecision* decision = (WebKitPolicyDecision*)g_hash_table_lookup(
        s->pending_policy, GUINT_TO_POINTER((guint)request_id));

    if (decision == NULL)
        return;

    g_hash_table_remove(s->pending_policy, GUINT_TO_POINTER((guint)request_id));

    if (allow)
        webkit_policy_decision_use(decision);
    else
        webkit_policy_decision_ignore(decision);

    g_object_unref(decision);
}

void ag_gtk_navigate(ag_gtk_handle handle, const char* url_utf8)
{
    if (!handle || !url_utf8) return;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL) return;

    webkit_web_view_load_uri(s->web_view, url_utf8);
}

void ag_gtk_load_html(ag_gtk_handle handle, const char* html_utf8, const char* base_url_utf8_or_null)
{
    if (!handle || !html_utf8) return;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL) return;

    webkit_web_view_load_html(s->web_view, html_utf8, base_url_utf8_or_null);
}

void ag_gtk_eval_js(ag_gtk_handle handle, uint64_t request_id, const char* script_utf8)
{
    if (!handle || request_id == 0 || !script_utf8) return;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL) return;

    eval_js_data* data = (eval_js_data*)malloc(sizeof(eval_js_data));
    if (data == NULL) return;
    data->state = s;
    data->request_id = request_id;

    webkit_web_view_run_javascript(s->web_view, script_utf8, NULL, on_eval_js_finish, data);
}

bool ag_gtk_go_back(ag_gtk_handle handle)
{
    if (!handle) return false;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL) return false;
    if (!webkit_web_view_can_go_back(s->web_view)) return false;
    webkit_web_view_go_back(s->web_view);
    return true;
}

bool ag_gtk_go_forward(ag_gtk_handle handle)
{
    if (!handle) return false;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL) return false;
    if (!webkit_web_view_can_go_forward(s->web_view)) return false;
    webkit_web_view_go_forward(s->web_view);
    return true;
}

bool ag_gtk_reload(ag_gtk_handle handle)
{
    if (!handle) return false;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL) return false;
    webkit_web_view_reload(s->web_view);
    return true;
}

void ag_gtk_stop(ag_gtk_handle handle)
{
    if (!handle) return;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL) return;
    webkit_web_view_stop_loading(s->web_view);
}

bool ag_gtk_can_go_back(ag_gtk_handle handle)
{
    if (!handle) return false;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL) return false;
    return webkit_web_view_can_go_back(s->web_view);
}

bool ag_gtk_can_go_forward(ag_gtk_handle handle)
{
    if (!handle) return false;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL) return false;
    return webkit_web_view_can_go_forward(s->web_view);
}

void* ag_gtk_get_webview_handle(ag_gtk_handle handle)
{
    if (!handle) return NULL;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached)) return NULL;
    return (void*)s->web_view;
}

/* ========== Cookie management ========== */

typedef struct
{
    shim_state* state;
    ag_gtk_cookies_get_cb callback;
    void* context;
    char* url;
} cookies_get_data;

static void on_cookies_get_finish(WebKitCookieManager* manager, GAsyncResult* result, gpointer user_data)
{
    cookies_get_data* data = (cookies_get_data*)user_data;

    GError* error = NULL;
    GList* cookies = webkit_cookie_manager_get_cookies_finish(manager, result, &error);

    if (error != NULL)
    {
        data->callback(data->context, "[]");
        g_error_free(error);
        free(data->url);
        free(data);
        return;
    }

    /* Build JSON array */
    GString* json = g_string_new("[");
    gboolean first = TRUE;
    for (GList* l = cookies; l != NULL; l = l->next)
    {
        SoupCookie* c = (SoupCookie*)l->data;
        if (!first) g_string_append_c(json, ',');
        first = FALSE;

        const char* name = soup_cookie_get_name(c);
        const char* value = soup_cookie_get_value(c);
        const char* domain = soup_cookie_get_domain(c);
        const char* path = soup_cookie_get_path(c);
        gboolean secure = soup_cookie_get_secure(c);
        gboolean http_only = soup_cookie_get_http_only(c);

        /* libsoup3 (webkit2gtk-4.1): soup_cookie_get_expires returns GDateTime* instead of SoupDate*. */
        GDateTime* expires = soup_cookie_get_expires(c);
        double expires_unix = expires ? (double)g_date_time_to_unix(expires) : -1.0;

        g_string_append_printf(json,
            "{\"name\":\"%s\",\"value\":\"%s\",\"domain\":\"%s\",\"path\":\"%s\","
            "\"expires\":%.3f,\"isSecure\":%s,\"isHttpOnly\":%s}",
            name ? name : "", value ? value : "", domain ? domain : "", path ? path : "/",
            expires_unix,
            secure ? "true" : "false",
            http_only ? "true" : "false");
    }
    g_string_append_c(json, ']');

    data->callback(data->context, json->str);

    g_string_free(json, TRUE);
    g_list_free_full(cookies, (GDestroyNotify)soup_cookie_free);
    free(data->url);
    free(data);
}

void ag_gtk_cookies_get(ag_gtk_handle handle, const char* url_utf8,
                         ag_gtk_cookies_get_cb callback, void* context)
{
    if (!handle || !callback) return;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL)
    {
        callback(context, "[]");
        return;
    }

    WebKitWebContext* web_ctx = webkit_web_view_get_context(s->web_view);
    WebKitCookieManager* cookie_mgr = webkit_web_context_get_cookie_manager(web_ctx);

    cookies_get_data* data = (cookies_get_data*)calloc(1, sizeof(cookies_get_data));
    data->state = s;
    data->callback = callback;
    data->context = context;
    data->url = url_utf8 ? strdup(url_utf8) : NULL;

    webkit_cookie_manager_get_cookies(cookie_mgr, url_utf8 ? url_utf8 : "",
                                       NULL, (GAsyncReadyCallback)on_cookies_get_finish, data);
}

void ag_gtk_cookie_set(ag_gtk_handle handle,
    const char* name, const char* value, const char* domain, const char* path,
    double expires_unix, bool is_secure, bool is_http_only,
    ag_gtk_cookie_op_cb callback, void* context)
{
    if (!handle || !callback) return;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL)
    {
        callback(context, false, "Detached");
        return;
    }

    SoupCookie* cookie = soup_cookie_new(
        name ? name : "", value ? value : "",
        domain ? domain : "", path ? path : "/",
        -1 /* max-age: session */);

    if (expires_unix > 0)
    {
        /* libsoup3 (used by webkit2gtk-4.1) replaced SoupDate with GDateTime. */
        GDateTime* date = g_date_time_new_from_unix_utc(expires_unix);
        if (date)
        {
            soup_cookie_set_expires(cookie, date);
            g_date_time_unref(date);
        }
    }

    soup_cookie_set_secure(cookie, is_secure);
    soup_cookie_set_http_only(cookie, is_http_only);

    WebKitWebContext* web_ctx = webkit_web_view_get_context(s->web_view);
    WebKitCookieManager* cookie_mgr = webkit_web_context_get_cookie_manager(web_ctx);
    webkit_cookie_manager_add_cookie(cookie_mgr, cookie, NULL, NULL, NULL);

    soup_cookie_free(cookie);
    callback(context, true, NULL);
}

void ag_gtk_cookie_delete(ag_gtk_handle handle,
    const char* name, const char* domain, const char* path,
    ag_gtk_cookie_op_cb callback, void* context)
{
    if (!handle || !callback) return;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL)
    {
        callback(context, false, "Detached");
        return;
    }

    SoupCookie* cookie = soup_cookie_new(
        name ? name : "", "",
        domain ? domain : "", path ? path : "/",
        0 /* expired */);

    WebKitWebContext* web_ctx = webkit_web_view_get_context(s->web_view);
    WebKitCookieManager* cookie_mgr = webkit_web_context_get_cookie_manager(web_ctx);
    webkit_cookie_manager_delete_cookie(cookie_mgr, cookie, NULL, NULL, NULL);

    soup_cookie_free(cookie);
    callback(context, true, NULL);
}

void ag_gtk_cookies_clear_all(ag_gtk_handle handle,
                               ag_gtk_cookie_op_cb callback, void* context)
{
    if (!handle || !callback) return;
    shim_state* s = (shim_state*)handle;
    if (atomic_load(&s->detached) || s->web_view == NULL)
    {
        callback(context, false, "Detached");
        return;
    }

    WebKitWebContext* web_ctx = webkit_web_view_get_context(s->web_view);
    WebKitWebsiteDataManager* data_mgr = webkit_web_context_get_website_data_manager(web_ctx);
    webkit_website_data_manager_clear(data_mgr, WEBKIT_WEBSITE_DATA_COOKIES, 0, NULL, NULL, NULL);

    callback(context, true, NULL);
}

/* ========== Environment options ========== */

void ag_gtk_set_enable_dev_tools(ag_gtk_handle handle, bool enable)
{
    if (!handle) return;
    shim_state* s = (shim_state*)handle;
    s->opt_enable_dev_tools = enable;
    if (!enable)
        atomic_store(&s->dev_tools_open, FALSE);

    /* Also apply to live WebView if already attached. */
    if (s->web_view != NULL && !atomic_load(&s->detached))
    {
        WebKitSettings* settings = webkit_web_view_get_settings(s->web_view);
        webkit_settings_set_enable_developer_extras(settings, enable);
    }
}

/* ========== DevTools runtime toggle ========== */

static void do_open_dev_tools(void* data)
{
    shim_state* s = (shim_state*)data;
    if (atomic_load(&s->detached) || s->web_view == NULL) return;
    if (!s->opt_enable_dev_tools) return;

    WebKitWebInspector* inspector = webkit_web_view_get_inspector(s->web_view);
    if (inspector == NULL) return;

    webkit_web_inspector_show(inspector);
    atomic_store(&s->dev_tools_open, TRUE);
}

static void do_close_dev_tools(void* data)
{
    shim_state* s = (shim_state*)data;
    if (atomic_load(&s->detached) || s->web_view == NULL) return;
    if (!s->opt_enable_dev_tools) return;

    WebKitWebInspector* inspector = webkit_web_view_get_inspector(s->web_view);
    if (inspector == NULL) return;

    webkit_web_inspector_close(inspector);
    atomic_store(&s->dev_tools_open, FALSE);
}

void ag_gtk_open_dev_tools(ag_gtk_handle handle)
{
    if (!handle) return;
    shim_state* s = (shim_state*)handle;
    run_on_gtk_thread(do_open_dev_tools, s);
}

void ag_gtk_close_dev_tools(ag_gtk_handle handle)
{
    if (!handle) return;
    shim_state* s = (shim_state*)handle;
    run_on_gtk_thread(do_close_dev_tools, s);
}

bool ag_gtk_is_dev_tools_open(ag_gtk_handle handle)
{
    if (!handle) return false;
    shim_state* s = (shim_state*)handle;
    return atomic_load(&s->dev_tools_open);
}

void ag_gtk_set_ephemeral(ag_gtk_handle handle, bool ephemeral)
{
    if (!handle) return;
    shim_state* s = (shim_state*)handle;
    s->opt_ephemeral = ephemeral;
}

void ag_gtk_set_user_agent(ag_gtk_handle handle, const char* ua_utf8_or_null)
{
    if (!handle) return;
    shim_state* s = (shim_state*)handle;

    free(s->opt_user_agent);
    s->opt_user_agent = ua_utf8_or_null ? strdup(ua_utf8_or_null) : NULL;

    /* Also update live WebView if already attached. */
    if (s->web_view != NULL && !atomic_load(&s->detached))
    {
        WebKitSettings* settings = webkit_web_view_get_settings(s->web_view);
        webkit_settings_set_user_agent(settings, ua_utf8_or_null);
    }
}

/* ========== Screenshot capture ========== */

typedef void (*ag_gtk_screenshot_cb)(void* context, const void* png_data, uint32_t png_len);

typedef struct {
    ag_gtk_screenshot_cb callback;
    void* context;
} screenshot_ctx;

static cairo_status_t png_write_to_byte_array(void* closure, const unsigned char* data, unsigned int length)
{
    GByteArray* array = (GByteArray*)closure;
    g_byte_array_append(array, data, length);
    return CAIRO_STATUS_SUCCESS;
}

static void on_snapshot_ready(GObject* source, GAsyncResult* result, gpointer user_data)
{
    screenshot_ctx* ctx = (screenshot_ctx*)user_data;
    GError* error = NULL;
    cairo_surface_t* surface = webkit_web_view_get_snapshot_finish(
        WEBKIT_WEB_VIEW(source), result, &error);

    if (error != NULL || surface == NULL)
    {
        if (error) g_error_free(error);
        ctx->callback(ctx->context, NULL, 0);
        free(ctx);
        return;
    }

    GByteArray* array = g_byte_array_new();
    cairo_status_t status = cairo_surface_write_to_png_stream(surface, png_write_to_byte_array, array);
    cairo_surface_destroy(surface);

    if (status != CAIRO_STATUS_SUCCESS || array->len == 0)
    {
        g_byte_array_free(array, TRUE);
        ctx->callback(ctx->context, NULL, 0);
        free(ctx);
        return;
    }

    ctx->callback(ctx->context, array->data, (uint32_t)array->len);
    g_byte_array_free(array, TRUE);
    free(ctx);
}

void ag_gtk_capture_screenshot(ag_gtk_handle handle, ag_gtk_screenshot_cb callback, void* context)
{
    if (!handle)
    {
        callback(context, NULL, 0);
        return;
    }
    shim_state* s = (shim_state*)handle;
    if (s->web_view == NULL || atomic_load(&s->detached))
    {
        callback(context, NULL, 0);
        return;
    }

    screenshot_ctx* ctx = (screenshot_ctx*)malloc(sizeof(screenshot_ctx));
    ctx->callback = callback;
    ctx->context = context;

    webkit_web_view_get_snapshot(
        s->web_view,
        WEBKIT_SNAPSHOT_REGION_VISIBLE,
        WEBKIT_SNAPSHOT_OPTIONS_NONE,
        NULL,
        on_snapshot_ready,
        ctx);
}

/* ========== Print to PDF ========== */

typedef void (*ag_gtk_pdf_cb)(void* context, const void* pdf_data, uint32_t pdf_len);

typedef struct {
    ag_gtk_pdf_cb callback;
    void* context;
    char* temp_path;
} pdf_ctx;

static void on_pdf_print_finished(WebKitPrintOperation* operation, gpointer user_data)
{
    (void)operation;
    pdf_ctx* ctx = (pdf_ctx*)user_data;
    if (!ctx) return;

    if (ctx->temp_path)
    {
        gchar* contents = NULL;
        gsize length = 0;
        if (g_file_get_contents(ctx->temp_path, &contents, &length, NULL) && contents)
        {
            ctx->callback(ctx->context, contents, (uint32_t)length);
            g_free(contents);
        }
        else
        {
            ctx->callback(ctx->context, NULL, 0);
        }
        g_unlink(ctx->temp_path);
        free(ctx->temp_path);
    }
    else
    {
        ctx->callback(ctx->context, NULL, 0);
    }
    free(ctx);
}

static void on_pdf_print_failed(WebKitPrintOperation* operation, GError* error, gpointer user_data)
{
    (void)operation;
    (void)error;
    pdf_ctx* ctx = (pdf_ctx*)user_data;
    if (!ctx) return;

    ctx->callback(ctx->context, NULL, 0);
    if (ctx->temp_path)
    {
        g_unlink(ctx->temp_path);
        free(ctx->temp_path);
    }
    free(ctx);
}

void ag_gtk_print_to_pdf(ag_gtk_handle handle, ag_gtk_pdf_cb callback, void* context)
{
    if (!handle)
    {
        callback(context, NULL, 0);
        return;
    }
    shim_state* s = (shim_state*)handle;
    if (s->web_view == NULL || atomic_load(&s->detached))
    {
        callback(context, NULL, 0);
        return;
    }

    pdf_ctx* ctx = (pdf_ctx*)calloc(1, sizeof(pdf_ctx));
    ctx->callback = callback;
    ctx->context = context;

    /* Generate a unique temp file path for the PDF output. */
    char temp_template[] = "/tmp/fulora_print_XXXXXX";
    int fd = mkstemp(temp_template);
    if (fd < 0)
    {
        callback(context, NULL, 0);
        free(ctx);
        return;
    }
    close(fd);
    /* Rename with .pdf extension for GTK print settings. */
    char pdf_path[256];
    snprintf(pdf_path, sizeof(pdf_path), "%s.pdf", temp_template);
    rename(temp_template, pdf_path);
    ctx->temp_path = strdup(pdf_path);

    WebKitPrintOperation* print_op = webkit_print_operation_new(s->web_view);

    GtkPrintSettings* settings = gtk_print_settings_new();
    gtk_print_settings_set_printer(settings, "Print to File");
    gtk_print_settings_set(settings, GTK_PRINT_SETTINGS_OUTPUT_FILE_FORMAT, "pdf");

    gchar* file_uri = g_filename_to_uri(ctx->temp_path, NULL, NULL);
    if (file_uri)
    {
        gtk_print_settings_set(settings, GTK_PRINT_SETTINGS_OUTPUT_URI, file_uri);
        g_free(file_uri);
    }
    webkit_print_operation_set_print_settings(print_op, settings);

    GtkPageSetup* page_setup = gtk_page_setup_new();
    gtk_page_setup_set_paper_size(page_setup, gtk_paper_size_new(GTK_PAPER_NAME_A4));
    webkit_print_operation_set_page_setup(print_op, page_setup);

    g_signal_connect(print_op, "finished", G_CALLBACK(on_pdf_print_finished), ctx);
    g_signal_connect(print_op, "failed", G_CALLBACK(on_pdf_print_failed), ctx);

    webkit_print_operation_print(print_op);

    g_object_unref(settings);
    g_object_unref(page_setup);
}

/* ========== Zoom ========== */

double ag_gtk_get_zoom(ag_gtk_handle handle)
{
    if (!handle) return 1.0;
    shim_state* s = (shim_state*)handle;
    if (s->web_view == NULL || atomic_load(&s->detached)) return 1.0;
    return webkit_web_view_get_zoom_level(s->web_view);
}

void ag_gtk_set_zoom(ag_gtk_handle handle, double zoom_factor)
{
    if (!handle) return;
    shim_state* s = (shim_state*)handle;
    if (s->web_view == NULL || atomic_load(&s->detached)) return;
    webkit_web_view_set_zoom_level(s->web_view, zoom_factor);
}

/* ========== Find in Page ========== */

typedef void (*ag_gtk_find_cb)(void* context, int32_t active_match_index, int32_t total_matches);

typedef struct {
    ag_gtk_find_cb callback;
    void* context;
    gulong counted_id;
    gulong failed_id;
} find_ctx;

static void on_counted_matches(WebKitFindController* controller, guint match_count, gpointer user_data)
{
    find_ctx* ctx = (find_ctx*)user_data;
    /* We report match_count; active index isn't directly available in WebKitGTK find API.
       Report 0 as active index when matches found. */
    int32_t active = match_count > 0 ? 0 : -1;
    if (ctx->counted_id) g_signal_handler_disconnect(controller, ctx->counted_id);
    if (ctx->failed_id) g_signal_handler_disconnect(controller, ctx->failed_id);
    ctx->callback(ctx->context, active, (int32_t)match_count);
    free(ctx);
}

static void on_failed_to_find(WebKitFindController* controller, gpointer user_data)
{
    find_ctx* ctx = (find_ctx*)user_data;
    if (ctx->counted_id) g_signal_handler_disconnect(controller, ctx->counted_id);
    if (ctx->failed_id) g_signal_handler_disconnect(controller, ctx->failed_id);
    ctx->callback(ctx->context, -1, 0);
    free(ctx);
}

void ag_gtk_find_text(ag_gtk_handle handle, const char* text, int case_sensitive, int forward, ag_gtk_find_cb callback, void* context)
{
    if (!handle)
    {
        callback(context, -1, 0);
        return;
    }
    shim_state* s = (shim_state*)handle;
    if (s->web_view == NULL || atomic_load(&s->detached))
    {
        callback(context, -1, 0);
        return;
    }

    WebKitFindController* fc = webkit_web_view_get_find_controller(s->web_view);

    find_ctx* ctx = (find_ctx*)calloc(1, sizeof(find_ctx));
    ctx->callback = callback;
    ctx->context = context;

    ctx->counted_id = g_signal_connect(fc, "counted-matches", G_CALLBACK(on_counted_matches), ctx);
    ctx->failed_id = g_signal_connect(fc, "failed-to-find-text", G_CALLBACK(on_failed_to_find), ctx);

    guint32 options = WEBKIT_FIND_OPTIONS_WRAP_AROUND;
    if (!case_sensitive) options |= WEBKIT_FIND_OPTIONS_CASE_INSENSITIVE;
    if (!forward) options |= WEBKIT_FIND_OPTIONS_BACKWARDS;

    webkit_find_controller_search(fc, text, options, G_MAXUINT);
    webkit_find_controller_count_matches(fc, text, options, G_MAXUINT);
}

void ag_gtk_stop_find(ag_gtk_handle handle)
{
    if (!handle) return;
    shim_state* s = (shim_state*)handle;
    if (s->web_view == NULL || atomic_load(&s->detached)) return;

    WebKitFindController* fc = webkit_web_view_get_find_controller(s->web_view);
    webkit_find_controller_search_finish(fc);
}

/* ========== Preload Scripts ========== */

static int64_t g_script_id_counter = 0;

const char* ag_gtk_add_user_script(ag_gtk_handle handle, const char* js)
{
    if (!handle || !js) return NULL;
    shim_state* s = (shim_state*)handle;
    if (s->web_view == NULL || atomic_load(&s->detached)) return NULL;

    WebKitUserContentManager* ucm = webkit_web_view_get_user_content_manager(s->web_view);
    WebKitUserScript* script = webkit_user_script_new(
        js,
        WEBKIT_USER_CONTENT_INJECT_ALL_FRAMES,
        WEBKIT_USER_SCRIPT_INJECT_AT_DOCUMENT_START,
        NULL, NULL);
    webkit_user_content_manager_add_script(ucm, script);
    webkit_user_script_unref(script);

    int64_t id = ++g_script_id_counter;
    char buf[32];
    snprintf(buf, sizeof(buf), "preload_%lld", (long long)id);
    return strdup(buf);
}

void ag_gtk_remove_all_user_scripts(ag_gtk_handle handle)
{
    if (!handle) return;
    shim_state* s = (shim_state*)handle;
    if (s->web_view == NULL || atomic_load(&s->detached)) return;

    WebKitUserContentManager* ucm = webkit_web_view_get_user_content_manager(s->web_view);
    webkit_user_content_manager_remove_all_scripts(ucm);
}
