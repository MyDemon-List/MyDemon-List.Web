export function defilerVersElement(elementId) {
    const element = document.getElementById(elementId);
    if (!element) {
        return;
    }

    const reduireAnimations = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    element.scrollIntoView({
        behavior: reduireAnimations ? "auto" : "smooth",
        block: "start"
    });
}
