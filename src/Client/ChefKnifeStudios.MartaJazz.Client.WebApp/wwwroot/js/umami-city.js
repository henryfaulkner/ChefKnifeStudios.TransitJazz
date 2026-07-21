// Tags an Umami pageview with the current city (from the URL hash, e.g. #marta).
// Every city switch does a full page reload, so this runs once per city view.
// Auto-track is off, so this manual call is the ONLY pageview — it must not be
// dropped if the (deferred) Umami script hasn't finished loading yet, so we
// retry briefly until `umami` is available.
window.trackCityView = function (city) {
    if (!city) return;

    var attempts = 0;
    var send = function () {
        if (typeof umami !== "undefined") {
            umami.track(function (props) {
                return Object.assign({}, props, {
                    url: "/" + city,
                    title: "City: " + city,
                });
            });
            return;
        }
        if (attempts++ < 50) setTimeout(send, 100); // retry up to ~5s
    };
    send();
};
