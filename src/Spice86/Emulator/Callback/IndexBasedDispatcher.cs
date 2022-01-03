using Spice86.Emulator.Errors;
using Spice86.Utils;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Spice86.Emulator.Callback
{
    /// <summary>
    /// Base class for most classes having to dispatch operations depending on a numeric value, like interrupts.
    /// </summary>
    public abstract class IndexBasedDispatcher<T> where T : ICheckedRunnable
    {
        protected Dictionary<int, T> _dispatchTable = new();
        public virtual void Run(int index)
        {
            var handler = _dispatchTable[index];
            if (handler == null)
            {
                throw GenerateUnhandledOperationException(index);
            }

            handler.Run();
        }

        public virtual void AddService(int index, T runnable)
        {
            this._dispatchTable.Add(index, runnable);
        }

        protected abstract UnhandledOperationException GenerateUnhandledOperationException(int index);
    }
}