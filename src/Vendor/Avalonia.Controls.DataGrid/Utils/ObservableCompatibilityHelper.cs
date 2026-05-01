// (c) Copyright Microsoft Corporation.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

#nullable disable

using System;

namespace Avalonia.Controls.Utils
{
    internal static class ObservableCompatibilityHelper
    {
        public static IDisposable Combine(IDisposable first, IDisposable second)
        {
            return Create(() =>
            {
                second?.Dispose();
                first?.Dispose();
            });
        }

        public static IDisposable Create(Action disposeAction)
        {
            return new CallbackDisposable(disposeAction);
        }

        public static IDisposable Subscribe<T>(IObservable<T> source, Action<T> onNext)
        {
            return source.Subscribe(new CallbackObserver<T>(onNext));
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private Action _disposeAction;

            public CallbackDisposable(Action disposeAction)
            {
                _disposeAction = disposeAction;
            }

            public void Dispose()
            {
                Action disposeAction = _disposeAction;
                _disposeAction = null;
                disposeAction?.Invoke();
            }
        }

        private sealed class CallbackObserver<T> : IObserver<T>
        {
            private readonly Action<T> _onNext;

            public CallbackObserver(Action<T> onNext)
            {
                _onNext = onNext;
            }

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
            }

            public void OnNext(T value)
            {
                _onNext(value);
            }
        }
    }
}