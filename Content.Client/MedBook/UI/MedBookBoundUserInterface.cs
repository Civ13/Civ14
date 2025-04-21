using Content.Shared.MedicalScanner;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Content.Shared._Shitmed.Targeting; // Shitmed Change

namespace Content.Client.MedBook.UI
{
    [UsedImplicitly]

    public sealed class MedBookBoundUserInterface : BoundUserInterface
    {
        [ViewVariables]
        private MedBookWindow? _window;

        public MedBookBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            _window = this.CreateWindow<MedBookWindow>();
            _window.OnBodyPartSelected += SendBodyPartMessage; // Shitmed Change
            _window.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        }

        protected override void ReceiveMessage(BoundUserInterfaceMessage message)
        {
            if (_window == null)
                return;

            if (message is not MedBookScannedUserMessage cast)
                return;

            _window.Populate(cast);
        }
        // Shitmed Change Start
        private void SendBodyPartMessage(TargetBodyPart? part, EntityUid target) => SendMessage(new MedBookPartMessage(EntMan.GetNetEntity(target), part ?? null));
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
                return;

            if (_window != null)
                _window.OnBodyPartSelected -= SendBodyPartMessage;

            _window?.Dispose();
        }

        // Shitmed Change End
    }
}
