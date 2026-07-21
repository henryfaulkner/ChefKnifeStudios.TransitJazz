// Tags an Umami pageview with an explicit url + title. Auto-track is off, so these
// manual calls are the ONLY pageviews — they must not be dropped if the (deferred)
// Umami script hasn't finished loading yet, so we retry briefly until it's available.
window.trackPageView = function (url, title) {
    if (!url) return;

    var attempts = 0;
    var send = function () {
        if (typeof umami !== "undefined") {
            umami.track(function (props) {
                return Object.assign({}, props, { url: url, title: title || url });
            });
            return;
        }
        if (attempts++ < 50) setTimeout(send, 100); // retry up to ~5s
    };
    send();
};

// City views live in the URL hash (e.g. #marta); each switch does a full reload,
// so this runs once per city view.
window.trackCityView = function (city) {
    if (!city) return;
    window.trackPageView("/" + city, "City: " + city);
};
