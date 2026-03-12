import {
  BridgeReadyTimeoutError,
  createBridgeClient,
  type BridgeClient,
  type BridgeReadyOptions,
  type BridgeRpc,
  withErrorNormalization,
  withLogging,
  type ErrorNormalizationOptions,
  type LoggingOptions,
} from "./index.js";

export interface BridgeProfileExceptionScope {
  id: string;
  reason?: string;
}

export interface BridgeProfileExtension {
  id: string;
  apply(bridge: BridgeClient, exceptionScope?: BridgeProfileExceptionScope): void;
}

export interface BridgeProfileOptions {
  timeoutMs?: number;
  pollIntervalMs?: number;
  enableLogging?: boolean;
  logging?: LoggingOptions;
  errorNormalization?: ErrorNormalizationOptions;
  installMock?: () => void;
  forceMock?: boolean;
  resolveRpc?: () => BridgeRpc | null;
  extensions?: readonly BridgeProfileExtension[];
  exceptionScope?: BridgeProfileExceptionScope;
}

export interface BridgeProfile {
  readonly bridge: BridgeClient;
  readonly exceptionScope?: BridgeProfileExceptionScope;
  readonly isMockMode: boolean;
  ready(options?: BridgeReadyOptions): Promise<void>;
}

function toReadyOptions(
  profileOptions: BridgeProfileOptions,
  overrideOptions?: BridgeReadyOptions,
): BridgeReadyOptions {
  return {
    timeoutMs: overrideOptions?.timeoutMs ?? profileOptions.timeoutMs,
    pollIntervalMs: overrideOptions?.pollIntervalMs ?? profileOptions.pollIntervalMs,
  };
}

export function createBridgeProfile(options: BridgeProfileOptions = {}): BridgeProfile {
  const bridge = createBridgeClient(options.resolveRpc);
  const exceptionScope = options.exceptionScope;

  if (options.enableLogging) {
    bridge.use(withLogging(options.logging));
  }
  bridge.use(withErrorNormalization(options.errorNormalization));

  for (const extension of options.extensions ?? []) {
    extension.apply(bridge, exceptionScope);
  }

  let mockInstalled = false;
  const installMock = () => {
    if (mockInstalled || !options.installMock) {
      return;
    }

    options.installMock();
    mockInstalled = true;
  };

  return {
    bridge,
    exceptionScope,
    get isMockMode() {
      return mockInstalled;
    },
    async ready(readyOptions) {
      if (options.forceMock) {
        installMock();
      }

      try {
        await bridge.ready(toReadyOptions(options, readyOptions));
        return;
      } catch (err) {
        if (!mockInstalled && options.installMock && err instanceof BridgeReadyTimeoutError) {
          installMock();
          await bridge.ready(toReadyOptions(options, readyOptions));
          return;
        }

        throw err;
      }
    },
  };
}
