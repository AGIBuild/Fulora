// BiometricShim.m — Minimal ObjC wrapper around LAContext for C# P/Invoke.
// Compile: xcrun clang -fobjc-arc -dynamiclib BiometricShim.m -o libAgibuildBiometric.dylib \
//          -framework Foundation -framework LocalAuthentication -arch arm64 -arch x86_64

#import <Foundation/Foundation.h>
#import <LocalAuthentication/LocalAuthentication.h>

// Callback signature for evaluatePolicy reply.
typedef void (*biometric_reply_cb)(void *user_data, int success, const char *error_msg);

// Returns 1 if biometrics are available, 0 otherwise.
// If available, *out_type is set to "touchid" or "faceid" (caller must NOT free).
// If not available, *out_error is set to an error description (caller must free).
int ag_biometric_check_availability(const char **out_type, char **out_error)
{
    @autoreleasepool {
        LAContext *ctx = [[LAContext alloc] init];
        NSError *error = nil;
        BOOL ok = [ctx canEvaluatePolicy:LAPolicyDeviceOwnerAuthenticationWithBiometrics error:&error];

        if (ok) {
            switch (ctx.biometryType) {
                case LABiometryTypeTouchID:
                    *out_type = "touchid";
                    break;
                case LABiometryTypeFaceID:
                    *out_type = "faceid";
                    break;
                default:
                    *out_type = "unknown";
                    break;
            }
            *out_error = NULL;
            return 1;
        } else {
            *out_type = NULL;
            if (error) {
                const char *msg = [[error localizedDescription] UTF8String];
                *out_error = msg ? strdup(msg) : NULL;
            } else {
                *out_error = strdup("Biometric evaluation not available");
            }
            return 0;
        }
    }
}

// Authenticates using biometrics. Calls reply_cb on completion (may be on background thread).
void ag_biometric_authenticate(const char *reason_utf8, void *user_data, biometric_reply_cb reply_cb)
{
    @autoreleasepool {
        LAContext *ctx = [[LAContext alloc] init];
        NSString *reason = reason_utf8
            ? [NSString stringWithUTF8String:reason_utf8]
            : @"Authenticate";

        [ctx evaluatePolicy:LAPolicyDeviceOwnerAuthenticationWithBiometrics
            localizedReason:reason
                      reply:^(BOOL success, NSError *error) {
            if (success) {
                reply_cb(user_data, 1, NULL);
            } else {
                const char *msg = error ? [[error localizedDescription] UTF8String] : "Authentication failed";
                const char *code = NULL;
                if (error) {
                    switch (error.code) {
                        case LAErrorUserCancel:
                        case LAErrorAppCancel:
                            code = "user_cancelled";
                            break;
                        case LAErrorUserFallback:
                            code = "user_fallback";
                            break;
                        case LAErrorAuthenticationFailed:
                            code = "auth_failed";
                            break;
                        case LAErrorBiometryLockout:
                            code = "lockout";
                            break;
                        default:
                            code = "unknown";
                            break;
                    }
                }
                // Send back error code as "code|message" format
                char buf[512];
                snprintf(buf, sizeof(buf), "%s|%s", code ?: "unknown", msg ?: "");
                reply_cb(user_data, 0, buf);
            }
        }];
    }
}

// Free a string allocated by this shim.
void ag_biometric_free(char *ptr)
{
    free(ptr);
}
