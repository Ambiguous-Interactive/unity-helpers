mergeInto(LibraryManager.library, {
  $BrowserConsumerStorageState: { descriptor: null, overridden: false },
  BrowserConsumerStorageSeedRaw__deps: ["$UTF8ToString"],
  BrowserConsumerStorageSeedRaw: function (key, value) {
    try {
      window.localStorage.setItem(UTF8ToString(key), UTF8ToString(value));
      return 1;
    } catch (error) {
      return 0;
    }
  },
  BrowserConsumerStorageSetBlocked__deps: ["$BrowserConsumerStorageState"],
  BrowserConsumerStorageSetBlocked: function (blocked) {
    try {
      if (blocked) {
        if (BrowserConsumerStorageState.overridden) {
          return 0;
        }
        BrowserConsumerStorageState.descriptor = Object.getOwnPropertyDescriptor(
          window,
          "localStorage"
        );
        Object.defineProperty(window, "localStorage", {
          configurable: true,
          get: function () {
            throw new Error("Consumer storage access denied");
          }
        });
        BrowserConsumerStorageState.overridden = true;
      } else if (BrowserConsumerStorageState.overridden) {
        if (BrowserConsumerStorageState.descriptor) {
          Object.defineProperty(window, "localStorage", BrowserConsumerStorageState.descriptor);
        } else {
          delete window.localStorage;
        }
        BrowserConsumerStorageState.descriptor = null;
        BrowserConsumerStorageState.overridden = false;
      }
      return 1;
    } catch (error) {
      return 0;
    }
  },
  BrowserConsumerReport: function (result) {
    if (window.browserConsumerResult !== undefined) {
      throw new Error("Browser consumer reported more than once");
    }
    window.browserConsumerResult = UTF8ToString(result);
  }
});
