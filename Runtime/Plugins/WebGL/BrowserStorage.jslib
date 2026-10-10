mergeInto(LibraryManager.library, {
  WUHBrowserStorageGetString__deps: [
    "$UTF8ToString",
    "$lengthBytesUTF8",
    "$stringToUTF8",
    "malloc",
    "free"
  ],
  WUHBrowserStorageGetString: function (key) {
    var buffer = 0;
    try {
      var value = window.localStorage.getItem(UTF8ToString(key));
      if (
        value === null ||
        /\0|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(^|[^\uD800-\uDBFF])[\uDC00-\uDFFF]/.test(value)
      ) {
        return 0;
      }
      var size = lengthBytesUTF8(value) + 1;
      buffer = _malloc(size);
      if (!buffer) {
        return 0;
      }
      stringToUTF8(value, buffer, size);
      return buffer;
    } catch (error) {
      if (buffer) {
        _free(buffer);
      }
      return 0;
    }
  },
  WUHBrowserStorageSetString__deps: ["$UTF8ToString"],
  WUHBrowserStorageSetString: function (key, value) {
    try {
      window.localStorage.setItem(UTF8ToString(key), UTF8ToString(value));
      return 1;
    } catch (error) {
      return 0;
    }
  },
  WUHBrowserStorageDeleteKey__deps: ["$UTF8ToString"],
  WUHBrowserStorageDeleteKey: function (key) {
    try {
      window.localStorage.removeItem(UTF8ToString(key));
      return 1;
    } catch (error) {
      return 0;
    }
  }
});
