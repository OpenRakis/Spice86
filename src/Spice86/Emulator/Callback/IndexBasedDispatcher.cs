using Spice86.Emulator.Errors;

using System;
using System.Collections.Generic;

namespace Spice86.Emulator.Callback
{
    /// <summary>
    /// Base class for most classes having to dispatch operations depending on a numeric value, like interrupts.
    /// </summary>
    public abstract class IndexBasedDispatcher<T> where T : ICallback<Action>
    {
        protected Dictionary<int, ICallback<Action>> _dispatchTable = new();

        public void AddService(int index, ICallback<Action> runnable)
        {
            this._dispatchTable.Add(index, runnable);
        }

        public void Run(int index)
        {
            var handler = _dispatchTable[index];
            if (handler == null)
            {
                throw GenerateUnhandledOperationException(index);
            }

            handler?.GetCallback().Invoke();
        }

        protected abstract UnhandledOperationException GenerateUnhandledOperationException(int index);
    }
}