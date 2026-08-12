namespace MyDemonList.Web.Services
{
    public sealed class NotificationSignalService
    {
        public event Action<int>? NotificationsModifiees;
        public event Action? NotificationsGlobalesModifiees;

        public void Signaler(int utilisateurId) => NotificationsModifiees?.Invoke(utilisateurId);
        public void SignalerTous() => NotificationsGlobalesModifiees?.Invoke();
    }
}
