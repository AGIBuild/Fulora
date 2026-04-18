using System.Runtime.InteropServices;
using System.Runtime.Versioning;

// CA1401: P/Invoke methods exposed internally for use by the adapter; surface is
// intentionally not public.
#pragma warning disable CA1401

namespace Agibuild.Fulora.Adapters.Gtk.Interop;

/// <summary>
/// P/Invoke bindings for the subset of GTK 3, WebKit2GTK 4.1, GObject, GLib, GIO,
/// libsoup and Cairo APIs required by <c>GtkWebViewAdapter</c>.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the hand-written <c>WebKitGtkShim.c</c> native layer. Every entry point
/// here is a direct call into a system library — no intermediate shim, no
/// <c>xcrun</c>/<c>pkg-config</c>/<c>gcc</c> build step.
/// </para>
/// <para>
/// Logical library names (the <c>Lib*</c> constants) are mapped to actual <c>.so</c>
/// files by <see cref="GtkLibraryResolver"/>. The Gtk adapter entry point must call
/// <see cref="GtkLibraryResolver.Register"/> once before any P/Invoke here is issued.
/// </para>
/// <para>
/// Callback signatures used with <c>g_signal_connect_data</c> or
/// <c>webkit_web_view_run_javascript</c> must be declared as
/// <c>[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]</c> static methods
/// in the adapter, and wrapped with <see cref="GtkSignal"/> for signal connections so
/// the managed state is kept alive through a <see cref="GCHandle"/>.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
internal static partial class GtkInterop
{
    // =====================================================================
    //  Logical library names (resolved by GtkLibraryResolver).
    // =====================================================================

    internal const string LibGtk = "libgtk";
    internal const string LibGdk = "libgdk";
    internal const string LibGLib = "libglib";
    internal const string LibGObject = "libgobject";
    internal const string LibGio = "libgio";
    internal const string LibWebKit = "libwebkit2gtk";
    internal const string LibSoup = "libsoup";
    internal const string LibCairo = "libcairo";

    // =====================================================================
    //  GLib — main loop, memory, errors, date-time.
    // =====================================================================

    [LibraryImport(LibGLib)]
    internal static partial IntPtr g_main_context_default();

    [LibraryImport(LibGLib)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool g_main_context_is_owner(IntPtr context);

    /// <summary>Schedule <paramref name="function"/> on the default main loop; runs once.</summary>
    /// <returns>The source id (non-zero).</returns>
    [LibraryImport(LibGLib)]
    internal static partial uint g_idle_add(IntPtr function, IntPtr data);

    [LibraryImport(LibGLib)]
    internal static partial void g_free(IntPtr mem);

    [LibraryImport(LibGLib)]
    internal static partial void g_error_free(IntPtr error);

    [LibraryImport(LibGLib)]
    internal static partial IntPtr g_date_time_new_from_unix_utc(long t);

    [LibraryImport(LibGLib)]
    internal static partial long g_date_time_to_unix(IntPtr datetime);

    [LibraryImport(LibGLib)]
    internal static partial void g_date_time_unref(IntPtr datetime);

    /// <summary>Free a <c>GList*</c> chain, invoking <paramref name="freeFunc"/> on each element.</summary>
    [LibraryImport(LibGLib)]
    internal static partial void g_list_free_full(IntPtr list, IntPtr freeFunc);

    /// <summary>Return the <c>data</c> field of a <c>GList</c> link. Inline equivalent of the macro.</summary>
    internal static IntPtr g_list_data(IntPtr list)
    {
        // GList layout: { gpointer data; GList* next; GList* prev; }
        return list == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(list, 0);
    }

    /// <summary>Return the <c>next</c> field of a <c>GList</c> link.</summary>
    internal static IntPtr g_list_next(IntPtr list)
    {
        return list == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(list, IntPtr.Size);
    }

    // =====================================================================
    //  GObject — reference counting and signals.
    // =====================================================================

    [LibraryImport(LibGObject)]
    internal static partial IntPtr g_object_ref(IntPtr @object);

    [LibraryImport(LibGObject)]
    internal static partial void g_object_unref(IntPtr @object);

    [LibraryImport(LibGObject, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial ulong g_signal_connect_data(
        IntPtr instance,
        string detailedSignal,
        IntPtr cHandler,
        IntPtr data,
        IntPtr destroyData,
        int connectFlags);

    [LibraryImport(LibGObject)]
    internal static partial void g_signal_handler_disconnect(IntPtr instance, ulong handlerId);

    // =====================================================================
    //  GIO — file I/O (used only by the print-to-PDF path).
    // =====================================================================

    [LibraryImport(LibGLib, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool g_file_get_contents(
        string filename,
        out IntPtr contents,
        out IntPtr length,
        IntPtr error);

    [LibraryImport(LibGLib, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr g_filename_to_uri(string filename, string? hostname, IntPtr error);

    // =====================================================================
    //  GTK 3 — windowing, containers, GtkPlug, printing.
    // =====================================================================

    /// <summary>Initialises GTK; safe to call multiple times.</summary>
    [LibraryImport(LibGtk)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool gtk_init_check(IntPtr argc, IntPtr argv);

    [LibraryImport(LibGtk)]
    internal static partial IntPtr gtk_plug_new(ulong socketId);

    [LibraryImport(LibGtk)]
    internal static partial void gtk_widget_destroy(IntPtr widget);

    [LibraryImport(LibGtk)]
    internal static partial void gtk_widget_show_all(IntPtr widget);

    [LibraryImport(LibGtk)]
    internal static partial void gtk_container_add(IntPtr container, IntPtr widget);

    [LibraryImport(LibGtk)]
    internal static partial IntPtr gtk_print_settings_new();

    [LibraryImport(LibGtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_print_settings_set(IntPtr settings, string key, string value);

    [LibraryImport(LibGtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void gtk_print_settings_set_printer(IntPtr settings, string printer);

    [LibraryImport(LibGtk)]
    internal static partial IntPtr gtk_page_setup_new();

    [LibraryImport(LibGtk)]
    internal static partial void gtk_page_setup_set_paper_size(IntPtr pageSetup, IntPtr paperSize);

    [LibraryImport(LibGtk, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr gtk_paper_size_new(string? name);

    // =====================================================================
    //  WebKit2GTK 4.1 — web view lifecycle and navigation.
    // =====================================================================

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_web_view_new_with_user_content_manager(IntPtr userContentManager);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_web_view_new();

    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_web_view_load_uri(IntPtr webView, string uri);

    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_web_view_load_html(IntPtr webView, string content, string? baseUri);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_web_view_get_uri(IntPtr webView);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_web_view_stop_loading(IntPtr webView);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_web_view_reload(IntPtr webView);

    [LibraryImport(LibWebKit)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool webkit_web_view_can_go_back(IntPtr webView);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_web_view_go_back(IntPtr webView);

    [LibraryImport(LibWebKit)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool webkit_web_view_can_go_forward(IntPtr webView);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_web_view_go_forward(IntPtr webView);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_web_view_get_context(IntPtr webView);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_web_view_get_settings(IntPtr webView);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_web_view_get_user_content_manager(IntPtr webView);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_web_view_get_inspector(IntPtr webView);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_web_view_get_find_controller(IntPtr webView);

    [LibraryImport(LibWebKit)]
    internal static partial double webkit_web_view_get_zoom_level(IntPtr webView);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_web_view_set_zoom_level(IntPtr webView, double zoomLevel);

    // =====================================================================
    //  WebKit2GTK — JavaScript evaluation.
    // =====================================================================

    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_web_view_run_javascript(
        IntPtr webView,
        string script,
        IntPtr cancellable,
        IntPtr callback,
        IntPtr userData);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_web_view_run_javascript_finish(
        IntPtr webView,
        IntPtr result,
        out IntPtr error);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_javascript_result_get_js_value(IntPtr jsResult);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_javascript_result_unref(IntPtr jsResult);

    /// <summary>Serialise a JSC value to JSON; caller must <c>g_free</c> the result.</summary>
    [LibraryImport(LibWebKit)]
    internal static partial IntPtr jsc_value_to_json(IntPtr value, uint indent);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr jsc_value_to_string(IntPtr value);

    // =====================================================================
    //  WebKit2GTK — policy / navigation decisions.
    // =====================================================================

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_policy_decision_use(IntPtr decision);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_policy_decision_ignore(IntPtr decision);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_navigation_policy_decision_get_navigation_action(IntPtr decision);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_navigation_policy_decision_get_frame_name(IntPtr decision);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_navigation_action_get_request(IntPtr action);

    [LibraryImport(LibWebKit)]
    internal static partial int webkit_navigation_action_get_navigation_type(IntPtr action);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_uri_request_get_uri(IntPtr request);

    // =====================================================================
    //  WebKit2GTK — settings.
    // =====================================================================

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_settings_set_enable_developer_extras(
        IntPtr settings,
        [MarshalAs(UnmanagedType.I1)] bool enabled);

    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_settings_set_user_agent(IntPtr settings, string? userAgent);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_settings_get_user_agent(IntPtr settings);

    // =====================================================================
    //  WebKit2GTK — web context, custom schemes, data manager.
    // =====================================================================

    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_web_context_register_uri_scheme(
        IntPtr context,
        string scheme,
        IntPtr callback,
        IntPtr userData,
        IntPtr userDataDestroyFunc);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_web_context_get_cookie_manager(IntPtr context);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_web_context_get_website_data_manager(IntPtr context);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_website_data_manager_clear(
        IntPtr manager,
        uint types,
        ulong timespan,
        IntPtr cancellable,
        IntPtr callback,
        IntPtr userData);

    // WebKitWebsiteDataTypes.WEBKIT_WEBSITE_DATA_COOKIES
    internal const uint WEBKIT_WEBSITE_DATA_COOKIES = 1u << 7;

    // =====================================================================
    //  WebKit2GTK — cookie manager (async).
    // =====================================================================

    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_cookie_manager_get_cookies(
        IntPtr manager,
        string uri,
        IntPtr cancellable,
        IntPtr callback,
        IntPtr userData);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_cookie_manager_get_cookies_finish(
        IntPtr manager,
        IntPtr result,
        out IntPtr error);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_cookie_manager_add_cookie(
        IntPtr manager,
        IntPtr cookie,
        IntPtr cancellable,
        IntPtr callback,
        IntPtr userData);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_cookie_manager_delete_cookie(
        IntPtr manager,
        IntPtr cookie,
        IntPtr cancellable,
        IntPtr callback,
        IntPtr userData);

    // =====================================================================
    //  WebKit2GTK — developer tools.
    // =====================================================================

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_web_inspector_show(IntPtr inspector);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_web_inspector_close(IntPtr inspector);

    // =====================================================================
    //  WebKit2GTK — find controller.
    // =====================================================================

    // WebKitFindOptions flags (subset).
    internal const uint WEBKIT_FIND_OPTIONS_NONE = 0;
    internal const uint WEBKIT_FIND_OPTIONS_CASE_INSENSITIVE = 1 << 0;
    internal const uint WEBKIT_FIND_OPTIONS_BACKWARDS = 1 << 2;
    internal const uint WEBKIT_FIND_OPTIONS_WRAP_AROUND = 1 << 3;

    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_find_controller_search(
        IntPtr controller,
        string searchText,
        uint findOptions,
        uint maxMatchCount);

    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void webkit_find_controller_count_matches(
        IntPtr controller,
        string searchText,
        uint findOptions,
        uint maxMatchCount);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_find_controller_search_finish(IntPtr controller);

    // =====================================================================
    //  WebKit2GTK — user content manager (preload scripts, message handlers).
    // =====================================================================

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_user_content_manager_new();

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_user_content_manager_add_script(
        IntPtr manager,
        IntPtr script);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_user_content_manager_remove_all_scripts(IntPtr manager);

    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool webkit_user_content_manager_register_script_message_handler(
        IntPtr manager,
        string name);

    // WebKitUserContentInjectedFrames / WebKitUserScriptInjectionTime.
    internal const int WEBKIT_USER_CONTENT_INJECT_ALL_FRAMES = 0;
    internal const int WEBKIT_USER_SCRIPT_INJECT_AT_DOCUMENT_START = 0;

    [LibraryImport(LibWebKit, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr webkit_user_script_new(
        string source,
        int injectedFrames,
        int injectionTime,
        IntPtr whitelist,
        IntPtr blacklist);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_user_script_unref(IntPtr script);

    // =====================================================================
    //  WebKit2GTK — snapshot (screenshot) and printing.
    // =====================================================================

    // WebKitSnapshotRegion / Options.
    internal const int WEBKIT_SNAPSHOT_REGION_VISIBLE = 0;
    internal const int WEBKIT_SNAPSHOT_OPTIONS_NONE = 0;

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_web_view_get_snapshot(
        IntPtr webView,
        int region,
        int options,
        IntPtr cancellable,
        IntPtr callback,
        IntPtr userData);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_web_view_get_snapshot_finish(
        IntPtr webView,
        IntPtr result,
        out IntPtr error);

    [LibraryImport(LibWebKit)]
    internal static partial IntPtr webkit_print_operation_new(IntPtr webView);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_print_operation_set_print_settings(IntPtr operation, IntPtr settings);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_print_operation_set_page_setup(IntPtr operation, IntPtr pageSetup);

    [LibraryImport(LibWebKit)]
    internal static partial void webkit_print_operation_print(IntPtr operation);

    // =====================================================================
    //  libsoup 3 — cookies (webkit2gtk-4.1 uses libsoup 3 with GDateTime).
    // =====================================================================

    [LibraryImport(LibSoup, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr soup_cookie_new(
        string name,
        string value,
        string domain,
        string path,
        int maxAge);

    [LibraryImport(LibSoup)]
    internal static partial void soup_cookie_free(IntPtr cookie);

    [LibraryImport(LibSoup)]
    internal static partial IntPtr soup_cookie_get_name(IntPtr cookie);

    [LibraryImport(LibSoup)]
    internal static partial IntPtr soup_cookie_get_value(IntPtr cookie);

    [LibraryImport(LibSoup)]
    internal static partial IntPtr soup_cookie_get_domain(IntPtr cookie);

    [LibraryImport(LibSoup)]
    internal static partial IntPtr soup_cookie_get_path(IntPtr cookie);

    [LibraryImport(LibSoup)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool soup_cookie_get_secure(IntPtr cookie);

    [LibraryImport(LibSoup)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool soup_cookie_get_http_only(IntPtr cookie);

    /// <summary>Returns a <c>GDateTime*</c> (libsoup 3) or <c>NULL</c> for session cookies.</summary>
    [LibraryImport(LibSoup)]
    internal static partial IntPtr soup_cookie_get_expires(IntPtr cookie);

    [LibraryImport(LibSoup)]
    internal static partial void soup_cookie_set_expires(IntPtr cookie, IntPtr expires);

    [LibraryImport(LibSoup)]
    internal static partial void soup_cookie_set_secure(
        IntPtr cookie,
        [MarshalAs(UnmanagedType.I1)] bool secure);

    [LibraryImport(LibSoup)]
    internal static partial void soup_cookie_set_http_only(
        IntPtr cookie,
        [MarshalAs(UnmanagedType.I1)] bool httpOnly);

    // =====================================================================
    //  Cairo — PNG write stream (screenshot path).
    // =====================================================================

    [LibraryImport(LibCairo)]
    internal static partial int cairo_surface_write_to_png_stream(
        IntPtr surface,
        IntPtr writeFunc,
        IntPtr closure);

    [LibraryImport(LibCairo)]
    internal static partial void cairo_surface_destroy(IntPtr surface);

    internal const int CAIRO_STATUS_SUCCESS = 0;

    // =====================================================================
    //  Helpers — managed-side utilities over the raw P/Invoke surface.
    // =====================================================================

    /// <summary>UTF-8 C string → managed <see cref="string"/>; returns <see langword="null"/> for NULL.</summary>
    internal static string? PtrToUtf8String(IntPtr ptr)
        => ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);

    /// <summary>UTF-8 C string → managed <see cref="string"/>; returns empty string for NULL.</summary>
    internal static string PtrToUtf8StringOrEmpty(IntPtr ptr)
        => ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
}
