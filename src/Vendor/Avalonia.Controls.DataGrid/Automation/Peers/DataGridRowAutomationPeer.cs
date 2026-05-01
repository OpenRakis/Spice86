using Avalonia.Controls;

namespace Avalonia.Automation.Peers
{
#if !DATAGRID_INTERNAL
    public
#endif
    class DataGridRowAutomationPeer : ControlAutomationPeer
    {
        public DataGridRowAutomationPeer(DataGridRow owner)
            : base(owner)
        {
        }

        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return AutomationControlType.DataItem;
        }

        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
    }
}
