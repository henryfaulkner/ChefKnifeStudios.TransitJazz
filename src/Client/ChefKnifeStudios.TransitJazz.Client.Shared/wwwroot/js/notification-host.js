const visibilityListeners = new Map();
let nextVisibilityListenerId = 0;

export function addVisibilityChangeListener(dotNetReference) {
    const id = ++nextVisibilityListenerId;
    const listener = () => {
        void dotNetReference.invokeMethodAsync("HandleHostVisibilityChanged", document.hidden).catch(() => { });
    };

    visibilityListeners.set(id, listener);
    document.addEventListener("visibilitychange", listener);
    return id;
}

export function removeVisibilityChangeListener(id) {
    const listener = visibilityListeners.get(id);
    if (listener) {
        document.removeEventListener("visibilitychange", listener);
        visibilityListeners.delete(id);
    }
}

const focusReturns = new WeakMap();

export function attachFocusReturn(host) {
    const state = { previous: null };
    state.listener = event => {
        if (!host.contains(event.target) || host.contains(event.relatedTarget)) {
            return;
        }

        const previous = event.relatedTarget;
        state.previous = previous instanceof HTMLElement && previous !== document.body &&
            previous.matches("button, input, select, textarea, a[href], [tabindex]:not([tabindex='-1'])") &&
            !previous.hasAttribute("disabled")
            ? previous
            : null;
    };

    document.addEventListener("focusin", state.listener);
    focusReturns.set(host, state);
}

export function focusIsInside(host) {
    return host.contains(document.activeElement);
}

export function restorePreviousFocus(host) {
    const state = focusReturns.get(host);
    const target = state?.previous;
    state.previous = null;

    if (target instanceof HTMLElement && target.isConnected && !target.hasAttribute("disabled")) {
        target.focus();
    }
}

export function detachFocusReturn(host) {
    const state = focusReturns.get(host);
    if (!state) {
        return;
    }

    document.removeEventListener("focusin", state.listener);
    focusReturns.delete(host);
}
