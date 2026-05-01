// (c) Copyright Microsoft Corporation.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved. 

#nullable disable

using Avalonia.Data;
using System;
using Avalonia.Controls.Utils;
using Avalonia.Metadata;

namespace Avalonia.Controls
{
    /// <summary>
    /// Represents a <see cref="T:Avalonia.Controls.DataGrid" /> column that can 
    /// bind to a property in the grid's data source.
    /// </summary>
#if !DATAGRID_INTERNAL
    public
#endif
    abstract class DataGridBoundColumn : DataGridColumn
    {
        private BindingBase _binding;

        /// <summary>
        /// Gets or sets the binding that associates the column with a property in the data source.
        /// </summary>
        //TODO Binding
        [AssignBinding]
        [InheritDataTypeFromItems(nameof(DataGrid.ItemsSource), AncestorType = typeof(DataGrid))]
        public virtual BindingBase Binding
        {
            get
            {
                return _binding;
            }
            set
            {
                if (_binding != value)
                {
                    if (OwningGrid != null && !OwningGrid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true))
                    {
                        // Edited value couldn't be committed, so we force a CancelEdit
                        OwningGrid.CancelEdit(DataGridEditingUnit.Row, raiseEvents: false);
                    }

                    _binding = value;

                    if (_binding != null)
                    {
                        if (_binding is BindingBase binding)
                        {
                            BindingMode mode = BindingCompatibilityHelper.GetMode(binding);
                            if (mode == BindingMode.OneWayToSource)
                            {
                                throw new InvalidOperationException("DataGridColumn doesn't support BindingMode.OneWayToSource. Use BindingMode.TwoWay instead.");
                            }

                            string path = BindingCompatibilityHelper.GetPath(binding);
                            if (!string.IsNullOrEmpty(path) && mode == BindingMode.Default)
                            {
                                BindingCompatibilityHelper.SetMode(binding, BindingMode.TwoWay);
                            }

                            if (BindingCompatibilityHelper.GetConverter(binding) == null && string.IsNullOrEmpty(BindingCompatibilityHelper.GetStringFormat(binding)))
                            {
                                BindingCompatibilityHelper.SetConverter(binding, DataGridValueConverter.Instance);
                            }
                        }

                        // Apply the new Binding to existing rows in the DataGrid
                        if (OwningGrid != null)
                        {
                            OwningGrid.OnColumnBindingChanged(this);
                        }
                    }

                    RemoveEditingElement();
                }
            }
        }

        /// <summary>
        /// The binding that will be used to get or set cell content for the clipboard.
        /// If the base ClipboardContentBinding is not explicitly set, this will return the value of Binding.
        /// </summary>
        public override BindingBase ClipboardContentBinding
        {
            get
            {
                return base.ClipboardContentBinding ?? Binding;
            }
            set
            {
                base.ClipboardContentBinding = value;
            }
        }

        //TODO Rename
        //TODO Validation
        protected sealed override Control GenerateEditingElement(DataGridCell cell, object dataItem, out BindingExpressionBase editBinding)
        {
            Control element = GenerateEditingElementDirect(cell, dataItem);
            editBinding = null;

            if (Binding != null)
            {
                editBinding = element.Bind(BindingTarget, Binding);
            }

            return element;
        }

        protected abstract Control GenerateEditingElementDirect(DataGridCell cell, object dataItem);

        protected AvaloniaProperty BindingTarget { get; set; }

        internal void SetHeaderFromBinding()
        {
            if (OwningGrid != null && OwningGrid.DataConnection.DataType != null
                && Header == null && Binding != null)
            {
                string path = BindingCompatibilityHelper.GetPath(Binding);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var header = OwningGrid.DataConnection.DataType.GetDisplayName(path);
                    if (header != null)
                    {
                        Header = header;
                    }
                }
            }
        }
    }
}
