export function setOpen(dialog, open) {
    if (!dialog?.isConnected) return;
    if (open && !dialog.open) dialog.showModal();
    else if (!open && dialog.open) dialog.close();
}
