import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(new URL("../ChefKnifeStudios.TransitJazz.Client.Shared/wwwroot/js/stats-dialog.js", import.meta.url), "utf8");
const { setOpen } = await import(`data:text/javascript,${encodeURIComponent(source)}`);

test("Dialog visibility is idempotent and detached dialogs cannot reopen", () => {
    let opens = 0;
    let closes = 0;
    const dialog = {
        isConnected: true,
        open: false,
        showModal() { this.open = true; opens++; },
        close() { this.open = false; closes++; }
    };

    setOpen(dialog, true);
    setOpen(dialog, true);
    assert.equal(dialog.open, true);
    setOpen(dialog, false);
    setOpen(dialog, false);
    assert.equal(dialog.open, false);

    dialog.isConnected = false;
    setOpen(dialog, true);
    setOpen(null, true);
    assert.equal(opens, 1);
    assert.equal(closes, 1);
});
