(() => {
    const cleInitialisation = "mydemonlist.langue-initialisee";
    const cookieInitialisation = "mdl_langue_initialisee";
    const languesSupportees = ["fr", "en", "es"];

    let langueInitialisee = document.cookie
        .split(";")
        .some(cookie => cookie.trim().startsWith(`${cookieInitialisation}=`));

    try {
        langueInitialisee ||= localStorage.getItem(cleInitialisation) === "1";
    } catch {
    }

    if (langueInitialisee) {
        return;
    }

    try {
        localStorage.setItem(cleInitialisation, "1");
    } catch {
    }

    const securise = location.protocol === "https:" ? "; Secure" : "";
    document.cookie = `${cookieInitialisation}=1; Path=/; Max-Age=31536000; SameSite=Lax${securise}`;

    const languesNavigateur = navigator.languages?.length
        ? navigator.languages
        : [navigator.language];
    const langueNavigateur = languesNavigateur
        .map(langue => langue?.split("-", 1)[0].toLowerCase())
        .find(langue => languesSupportees.includes(langue)) ?? "en";

    const segments = location.pathname.split("/");
    const langueUrl = languesSupportees.includes(segments[1]?.toLowerCase())
        ? segments[1].toLowerCase()
        : null;

    if (langueUrl === langueNavigateur) {
        return;
    }

    if (langueUrl) {
        segments.splice(1, 1);
    }

    const cheminSansLangue = `/${segments.slice(1).join("/")}`;
    const cheminLocalise = cheminSansLangue === "/"
        ? `/${langueNavigateur}/`
        : `/${langueNavigateur}${cheminSansLangue}`;

    location.replace(`${cheminLocalise}${location.search}${location.hash}`);
})();
