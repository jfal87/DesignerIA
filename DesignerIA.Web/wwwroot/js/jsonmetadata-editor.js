window.designeriaDraft = {
    focus: function (element) {
        element?.focus();
    },
    selectFunction: function (element, start, length) {
        element.focus();
        element.setSelectionRange(start, start + length);
        const line = element.value.slice(0, start).split("\n").length - 1;
        const lineHeight = parseFloat(getComputedStyle(element).lineHeight) || 20;
        element.scrollTop = Math.max(0, (line - 2) * lineHeight);
    },
    saveJson: function (content, name) {
        const url = URL.createObjectURL(new Blob([content], { type: "application/json;charset=utf-8" }));
        const link = document.createElement("a");
        link.href = url;
        link.download = (name || "borrador-vista").replace(/[^a-zA-Z0-9_-]/g, "_") + ".json";
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    },
    focusDialog: function (dialog) {
        dialog.querySelector("input,button")?.focus();
        dialog.addEventListener("keydown", function (event) {
            if (event.key !== "Tab") return;
            const controls = Array.from(dialog.querySelectorAll("input:not(:disabled),button:not(:disabled)"));
            if (!controls.length) return;
            const first = controls[0], last = controls[controls.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        });
    }
};
