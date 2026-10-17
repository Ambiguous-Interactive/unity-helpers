mergeInto(LibraryManager.library, {
  BrowserConsumerReport: function (result) {
    if (window.browserConsumerResult !== undefined) {
      throw new Error("Browser consumer reported more than once");
    }
    window.browserConsumerResult = UTF8ToString(result);
  }
});
