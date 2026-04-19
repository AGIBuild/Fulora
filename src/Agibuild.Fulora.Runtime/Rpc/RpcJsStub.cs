namespace Agibuild.Fulora.Rpc;

/// <summary>
/// JavaScript stub injected into every WebView page that exposes the
/// <c>window.agWebView.rpc</c> facade (invoke / handle / batch / async iterators
/// / cancellation). Kept as a single string constant so it can be referenced by
/// the runtime, integration tests, and tooling without re-encoding the file.
/// </summary>
internal static class RpcJsStub
{
    public const string JsStub = """
        (function() {
            if (window.agWebView && window.agWebView.rpc) return;
            if (!window.agWebView) window.agWebView = {};
            var pending = {};
            var handlers = {};
            var nextId = 0;
            function post(msg) {
                if (window.chrome && window.chrome.webview) {
                    window.chrome.webview.postMessage(msg);
                } else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.agibuildWebView) {
                    window.webkit.messageHandlers.agibuildWebView.postMessage(msg);
                }
            }
            window.agWebView.rpc = {
                _uint8ToBase64: function(bytes) {
                    var binary = '';
                    for (var i = 0; i < bytes.length; i++) {
                        binary += String.fromCharCode(bytes[i]);
                    }
                    return btoa(binary);
                },
                _base64ToUint8: function(base64) {
                    var binary = atob(base64 || '');
                    var bytes = new Uint8Array(binary.length);
                    for (var i = 0; i < binary.length; i++) {
                        bytes[i] = binary.charCodeAt(i);
                    }
                    return bytes;
                },
                _encodeBinaryPayload: function(value) {
                    if (value === null || value === undefined) return value;
                    if (value instanceof Uint8Array) {
                        return window.agWebView.rpc._uint8ToBase64(value);
                    }
                    if (Array.isArray(value)) {
                        var mapped = new Array(value.length);
                        for (var i = 0; i < value.length; i++) {
                            mapped[i] = window.agWebView.rpc._encodeBinaryPayload(value[i]);
                        }
                        return mapped;
                    }
                    if (typeof value === 'object') {
                        var result = {};
                        for (var key in value) {
                            if (Object.prototype.hasOwnProperty.call(value, key)) {
                                result[key] = window.agWebView.rpc._encodeBinaryPayload(value[key]);
                            }
                        }
                        return result;
                    }
                    return value;
                },
                _decodeBinaryResult: function(value) {
                    if (typeof value !== 'string') return value;
                    return window.agWebView.rpc._base64ToUint8(value);
                },
                invoke: function(method, params, signal) {
                    return new Promise(function(resolve, reject) {
                        var id = '__js_' + (nextId++);
                        pending[id] = { resolve: resolve, reject: reject };
                        var encodedParams = window.agWebView.rpc._encodeBinaryPayload(params);
                        post(JSON.stringify({ jsonrpc: '2.0', id: id, method: method, params: encodedParams }));
                        if (signal) {
                            var onAbort = function() {
                                post(JSON.stringify({ jsonrpc: '2.0', method: '$/cancelRequest', params: { id: id } }));
                            };
                            if (signal.aborted) {
                                onAbort();
                            } else {
                                signal.addEventListener('abort', onAbort, { once: true });
                            }
                        }
                    });
                },
                handle: function(method, handler) {
                    handlers[method] = handler;
                },
                _dispatch: function(jsonStr) {
                    var msg = JSON.parse(jsonStr);
                    if (msg.method && handlers[msg.method]) {
                        try {
                            var result = handlers[msg.method](msg.params);
                            if (msg.id == null) return;
                            if (result && typeof result.then === 'function') {
                                result.then(function(r) {
                                    post(JSON.stringify({ jsonrpc: '2.0', id: msg.id, result: r }));
                                }).catch(function(e) {
                                    post(JSON.stringify({ jsonrpc: '2.0', id: msg.id, error: { code: -32603, message: e.message || 'Error' } }));
                                });
                            } else {
                                post(JSON.stringify({ jsonrpc: '2.0', id: msg.id, result: result }));
                            }
                        } catch(e) {
                            if (msg.id != null) post(JSON.stringify({ jsonrpc: '2.0', id: msg.id, error: { code: -32603, message: e.message || 'Error' } }));
                        }
                    }
                },
                batch: function(calls) {
                    var requests = [];
                    var ids = [];
                    for (var i = 0; i < calls.length; i++) {
                        var id = '__js_' + (nextId++);
                        ids.push(id);
                        requests.push({
                            jsonrpc: '2.0',
                            id: id,
                            method: calls[i].method,
                            params: window.agWebView.rpc._encodeBinaryPayload(calls[i].params)
                        });
                    }
                    var resultPromises = ids.map(function(id) {
                        return new Promise(function(resolve, reject) {
                            pending[id] = { resolve: resolve, reject: reject };
                        });
                    });
                    post(JSON.stringify(requests));
                    return Promise.all(resultPromises);
                },
                _onResponse: function(jsonStr) {
                    var msg = JSON.parse(jsonStr);
                    function resolveItem(item) {
                        var p = pending[item.id];
                        if (p) {
                            delete pending[item.id];
                            if (item.error) {
                                p.reject(new Error(item.error.message || 'RPC error'));
                            } else {
                                p.resolve(item.result);
                            }
                        }
                    }
                    if (Array.isArray(msg)) {
                        for (var i = 0; i < msg.length; i++) resolveItem(msg[i]);
                    } else {
                        resolveItem(msg);
                    }
                },
                _createAsyncIterable: function(method, params) {
                    var rpc = window.agWebView.rpc;
                    return {
                        [Symbol.asyncIterator]: function() {
                            var token = null;
                            var buffer = [];
                            var done = false;
                            var initPromise = rpc.invoke(method, params).then(function(r) {
                                token = r.token;
                                if (r.values) { for (var i = 0; i < r.values.length; i++) buffer.push(r.values[i]); }
                                if (r.finished) done = true;
                            });
                            return {
                                next: function() {
                                    return initPromise.then(function() {
                                        if (buffer.length > 0) return { value: buffer.shift(), done: false };
                                        if (done) return { value: undefined, done: true };
                                        return rpc.invoke('$/enumerator/next/' + token).then(function(r) {
                                            if (r.finished) { done = true; return { value: undefined, done: true }; }
                                            if (r.values && r.values.length > 0) return { value: r.values[0], done: false };
                                            return { value: undefined, done: true };
                                        });
                                    });
                                },
                                return: function() {
                                    if (token && !done) {
                                        done = true;
                                        post(JSON.stringify({ jsonrpc: '2.0', method: '$/enumerator/abort', params: { token: token } }));
                                    }
                                    return Promise.resolve({ value: undefined, done: true });
                                }
                            };
                        }
                    };
                }
            };
        })();
        """;
}
