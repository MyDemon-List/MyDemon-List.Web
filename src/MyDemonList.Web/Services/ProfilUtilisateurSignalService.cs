namespace MyDemonList.Web.Services;

public sealed class ProfilUtilisateurSignalService
{
    public event Action<int, string?>? DrapeauModifie;

    public void SignalerDrapeauModifie(int utilisateurId, string? codePays) =>
        DrapeauModifie?.Invoke(utilisateurId, codePays);
}
