const SELECTEUR_INVITATION_DISCORD = ".btn-invite-discord";

let gestionnaireAuthentification = null;
let observateurDom = null;
let frameMiroir = null;
const nettoyagesGif = new Map();

function configurerGifDiscord(ancre) {
    if (ancre.dataset.gifInit) {
        return;
    }

    const image = ancre.querySelector("img.gif-hover");
    if (!image) {
        return;
    }

    const source = image.getAttribute("data-src") || "";
    if (!source) {
        console.error("gif-hover: data-src manquant.");
        return;
    }

    ancre.dataset.gifInit = "1";

    const urlAnimee = new URL(source, document.baseURI).toString();
    const prechargement = new Image();
    prechargement.crossOrigin = "anonymous";

    prechargement.onload = () => {
        if (!ancre.isConnected) {
            return;
        }

        try {
            const canvas = document.createElement("canvas");
            canvas.width = prechargement.width;
            canvas.height = prechargement.height;
            canvas.getContext("2d").drawImage(prechargement, 0, 0);

            image.dataset.animated = urlAnimee;
            image.dataset.still = canvas.toDataURL("image/png");
            image.src = image.dataset.still;
        } catch (erreur) {
            console.warn("gif-hover: fallback GIF direct.", erreur);
            image.dataset.animated = urlAnimee;
            image.removeAttribute("data-still");
            image.src = urlAnimee;
        }
    };

    prechargement.onerror = () => console.error("gif-hover: échec chargement", urlAnimee);
    prechargement.src = urlAnimee;

    let minuterieArret;
    let minuterieArretForce;
    let lectureEnCours = false;
    let debutLecture = 0;

    function arreter() {
        lectureEnCours = false;
        clearTimeout(minuterieArret);
        clearTimeout(minuterieArretForce);

        if (image.dataset.still) {
            image.src = image.dataset.still;
        }
    }

    function lire() {
        if (lectureEnCours || !image.dataset.animated) {
            return;
        }

        lectureEnCours = true;
        debutLecture = performance.now();

        const separateur = image.dataset.animated.includes("?") ? "&" : "?";
        image.src = `${image.dataset.animated}${separateur}cb=${Date.now()}`;

        const duree = Number.parseInt(ancre.dataset.duration || "0", 10);
        clearTimeout(minuterieArretForce);
        if (duree > 0) {
            minuterieArretForce = setTimeout(arreter, duree);
        }
    }

    function arreterApresBoucle() {
        if (!lectureEnCours) {
            return;
        }

        const dureeBoucle = Math.max(50, Number.parseInt(ancre.dataset.loopMs || "1200", 10));
        const tempsEcoule = performance.now() - debutLecture;
        const tempsRestant = dureeBoucle - (tempsEcoule % dureeBoucle);

        clearTimeout(minuterieArret);
        minuterieArret = setTimeout(arreter, tempsRestant);
    }

    function conserverLecture() {
        clearTimeout(minuterieArret);
    }

    ancre.addEventListener("mouseenter", lire);
    ancre.addEventListener("focusin", lire);
    ancre.addEventListener("mouseleave", arreterApresBoucle);
    ancre.addEventListener("focusout", arreterApresBoucle);
    ancre.addEventListener("mouseenter", conserverLecture);
    ancre.addEventListener("focusin", conserverLecture);

    nettoyagesGif.set(ancre, () => {
        arreter();
        ancre.removeEventListener("mouseenter", lire);
        ancre.removeEventListener("focusin", lire);
        ancre.removeEventListener("mouseleave", arreterApresBoucle);
        ancre.removeEventListener("focusout", arreterApresBoucle);
        ancre.removeEventListener("mouseenter", conserverLecture);
        ancre.removeEventListener("focusin", conserverLecture);
        ancre.removeAttribute("data-gif-init");
    });
}

function initialiserGifsDiscord() {
    for (const [ancre, nettoyer] of nettoyagesGif) {
        if (!ancre.isConnected) {
            nettoyer();
            nettoyagesGif.delete(ancre);
        }
    }

    document.querySelectorAll(SELECTEUR_INVITATION_DISCORD).forEach(configurerGifDiscord);
}

function actualiserMiroirNavigation() {
    const indicateur = document.getElementById("nav-indicator");
    const miroir = document.getElementById("nav-mirror");

    if (indicateur && miroir) {
        const navigation = indicateur.closest("nav");
        if (navigation) {
            const cleCible = navigation.dataset.indicatorKey || "";
            const cible = Array.from(navigation.querySelectorAll("a.nav-item"))
                .find(element => element.dataset.navKey === cleCible);

            if (!indicateur.classList.contains("indicator--hidden") && cible && cible.getClientRects().length > 0) {
                const rectangleNavigation = navigation.getBoundingClientRect();
                const rectangleCible = cible.getBoundingClientRect();
                const gauche = rectangleCible.left - rectangleNavigation.left;
                const largeur = rectangleCible.width;

                if (indicateur.style.left !== `${gauche.toFixed(2)}px`) {
                    indicateur.style.left = `${gauche.toFixed(2)}px`;
                }
                if (indicateur.style.width !== `${largeur.toFixed(2)}px`) {
                    indicateur.style.width = `${largeur.toFixed(2)}px`;
                }

                indicateur.classList.add("indicator--positioned");
            } else {
                indicateur.classList.remove("indicator--positioned");
            }

            let decoupe;

            if (!indicateur.classList.contains("indicator--positioned")) {
                decoupe = "inset(0 100% 0 100%)";
            } else {
                const rectangleNavigation = navigation.getBoundingClientRect();
                const rectangleIndicateur = indicateur.getBoundingClientRect();
                const gauche = Math.max(0, rectangleIndicateur.left - rectangleNavigation.left);
                const droite = Math.max(0, rectangleNavigation.right - rectangleIndicateur.right);
                decoupe = `inset(0 ${droite.toFixed(2)}px 0 ${gauche.toFixed(2)}px round 20px)`;
            }

            if (miroir.style.clipPath !== decoupe) {
                miroir.style.clipPath = decoupe;
            }
        }
    }

    frameMiroir = requestAnimationFrame(actualiserMiroirNavigation);
}

export function initialiserNavMenu() {
    if (!gestionnaireAuthentification) {
        gestionnaireAuthentification = evenement => {
            if (evenement.data === "auth:done") {
                location.reload();
            }
        };

        window.addEventListener("message", gestionnaireAuthentification);
    }

    initialiserGifsDiscord();

    if (!observateurDom) {
        observateurDom = new MutationObserver(initialiserGifsDiscord);
        observateurDom.observe(document.body, { childList: true, subtree: true });
    }

    if (frameMiroir === null) {
        frameMiroir = requestAnimationFrame(actualiserMiroirNavigation);
    }
}

export function detruireNavMenu() {
    if (gestionnaireAuthentification) {
        window.removeEventListener("message", gestionnaireAuthentification);
        gestionnaireAuthentification = null;
    }

    observateurDom?.disconnect();
    observateurDom = null;

    if (frameMiroir !== null) {
        cancelAnimationFrame(frameMiroir);
        frameMiroir = null;
    }

    for (const nettoyer of nettoyagesGif.values()) {
        nettoyer();
    }
    nettoyagesGif.clear();
}
