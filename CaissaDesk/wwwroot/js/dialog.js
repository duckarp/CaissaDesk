// Focus management for modal dialogs (used by ModalDialog.razor). Offline, no dependencies.
// open(): remembers the focused element, moves focus into the dialog and keeps Tab inside it.
// close(): restores focus to the element that was focused before the dialog opened.
(function () {
	"use strict";

	var FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]):not([type="hidden"]), ' +
		'select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
	var stack = [];
	var nextId = 1;

	function focusables(root) {
		return Array.prototype.filter.call(root.querySelectorAll(FOCUSABLE), function (el) {
			return el.offsetParent !== null || el === document.activeElement;
		});
	}

	function topEntry() {
		return stack.length ? stack[stack.length - 1] : null;
	}

	function trapTab(e) {
		if (e.key !== "Tab") return;
		var top = topEntry();
		var dialog = e.currentTarget;
		if (!top || top.el !== dialog) return;
		var items = focusables(dialog);
		if (items.length === 0) {
			e.preventDefault();
			dialog.focus();
			return;
		}
		var first = items[0];
		var last = items[items.length - 1];
		var active = document.activeElement;
		if (e.shiftKey && (active === first || active === dialog)) {
			e.preventDefault();
			last.focus();
		} else if (!e.shiftKey && active === last) {
			e.preventDefault();
			first.focus();
		}
	}

	function initialTarget(dialog) {
		var explicit = dialog.querySelector("[data-autofocus]");
		if (explicit) return explicit;
		var body = dialog.querySelector(".modal-body");
		var inBody = body ? focusables(body) : [];
		return inBody.length ? inBody[0] : dialog;
	}

	window.caissaDialog = {
		open: function (dialog) {
			if (!dialog) return 0;
			var id = nextId++;
			stack.push({ id: id, el: dialog, returnTo: document.activeElement });
			dialog.addEventListener("keydown", trapTab);
			var target = initialTarget(dialog);
			try { target.focus(); } catch (err) { dialog.focus(); }
			return id;
		},
		close: function (id) {
			for (var i = stack.length - 1; i >= 0; i--) {
				if (stack[i].id !== id) continue;
				var entry = stack.splice(i, 1)[0];
				entry.el.removeEventListener("keydown", trapTab);
				var back = entry.returnTo;
				if (back && back.isConnected && typeof back.focus === "function") {
					back.focus();
				}
				return;
			}
		}
	};
})();
