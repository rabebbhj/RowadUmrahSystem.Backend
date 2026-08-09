window.showRowadToast = function (title, message, type) {
    type = type || "success";

    let bg = "#ffffff";
    let border = "#c9a24d";
    let icon = "🔔";

    if (type === "success") {
        border = "#16a34a";
        icon = "✅";
    }

    if (type === "danger") {
        border = "#dc2626";
        icon = "❌";
    }

    if (type === "warning") {
        border = "#d97706";
        icon = "⚠️";
    }

    if (type === "info") {
        border = "#2563eb";
        icon = "ℹ️";
    }

    let container = document.getElementById("rowad-toast-container");

    if (!container) {
        container = document.createElement("div");
        container.id = "rowad-toast-container";

        container.style.position = "fixed";
        container.style.top = "90px";
        container.style.right = "24px";
        container.style.zIndex = "999999";
        container.style.display = "flex";
        container.style.flexDirection = "column";
        container.style.gap = "12px";

        document.body.appendChild(container);
    }

    const toast = document.createElement("div");

    toast.style.minWidth = "320px";
    toast.style.maxWidth = "420px";
    toast.style.background = bg;
    toast.style.border = "1px solid #eadfcd";
    toast.style.borderRight = "6px solid " + border;
    toast.style.borderRadius = "18px";
    toast.style.padding = "16px 18px";
    toast.style.boxShadow = "0 18px 45px rgba(0,0,0,.18)";
    toast.style.color = "#2f2a22";
    toast.style.fontFamily = "Segoe UI, Tahoma, Arial, sans-serif";

    toast.innerHTML =
        '<div style="font-weight:800;margin-bottom:6px;">' +
        icon + " " + title +
        '</div>' +
        '<div style="font-size:14px;color:#7a7164;">' +
        message +
        '</div>';

    container.appendChild(toast);

    setTimeout(function () {
        toast.style.opacity = "0";
        toast.style.transform = "translateX(30px)";
        toast.style.transition = "all .3s ease";

        setTimeout(function () {
            toast.remove();
        }, 300);
    }, 4000);
};