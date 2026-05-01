using Avalonia.Automation.Peers;

namespace Avalonia.Controls.Automation.Peers;

#if !DATAGRID_INTERNAL
public
#endif
class DataGridAutomationPeer : ControlAutomationPeer
{
    public DataGridAutomationPeer(DataGrid owner)
        : base(owner)
    {
    }

    public new DataGrid Owner => (DataGrid)base.Owner;

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.DataGrid;
    }
}
