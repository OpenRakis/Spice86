using System;
using System.Collections;
using System.Collections.Generic;
using static SDLSharp.NativeMethods;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;

namespace SDLSharp
{
    public class EventQueue : IEnumerable<Event>
    {
        internal EventQueue() { }

        public bool Poll(out Event ev)
        {
            return SDL_PollEvent(out ev) != 0;
        }

        public void Wait(out Event ev)
        {
            ErrorIfZero(SDL_WaitEvent(out ev));
        }

        public bool Wait(out Event ev, int timeout)
        {
            if (SDL_WaitEventTimeout(out ev, timeout) != 0)
            {
                return true;
            }
            else
            {
                SDLException? error = GetError();
                if (error != null)
                    throw error;
                return false;
            }
        }

        public unsafe int Peep(
          Span<Event> events,
          EventType minType = EventType.FirstEvent,
          EventType maxType = EventType.LastEvent,
          bool remove = false
        )
        {
            fixed (Event* ptr = events)
                return ErrorIfNegative(SDL_PeepEvents(ptr, events.Length, remove ? SDL_eventaction.Peek : SDL_eventaction.Get, (uint)minType, (uint)maxType));
        }

        public bool Has(EventType type)
          => SDL_HasEvent((uint)type) == SDL_Bool.True;
        public bool Has(EventType start, EventType end)
          => SDL_HasEvents((uint)start, (uint)end) == SDL_Bool.True;

        public void Push(in QuitEvent ev)
          => Push(new Event() { quit = ev, type = EventType.Quit });

        public void Push(in UserEvent ev)
          => Push(new Event() { user = ev, type = EventType.UserEvent });

        public void Push(in SysWMEvent ev)
          => Push(new Event() { syswm = ev, type = EventType.SysWMEvent });

        public bool Push(in Event ev)
        {
            return ErrorIfNegative(SDL_PushEvent(ev)) != 0;
        }

        public void Push(object obj)
        {
            int id = System.Threading.Interlocked.Increment(ref SDL.eventObjCounter);
            SDL.eventObjects.Add(id, obj);

            Push(new UserEvent()
            {
                type = EventType.UserEvent,
                data1 = (IntPtr)id,
            });
        }

        public unsafe int Push(
          Span<Event> events,
          EventType minType = EventType.FirstEvent,
          EventType maxType = EventType.LastEvent
        )
        {
            fixed (Event* ptr = &MemoryMarshal.GetReference(events))
                return ErrorIfNegative(SDL_PeepEvents(ptr, events.Length, SDL_eventaction.Add, (uint)minType, (uint)maxType));
        }

        public void Pump()
        {
            SDL_PumpEvents();
        }

        public void Remove(EventType type)
          => SDL_FlushEvent((uint)type);
        public void Remove(EventType start, EventType end)
          => SDL_FlushEvents((uint)start, (uint)end);


        public void Filter(EventFilter filter)
        {
            ExceptionDispatchInfo? edi = null;

            SDL_EventFilter f = (IntPtr ud, ref Event v) =>
            {
                if (edi != null) return 1;
                try
                {
                    return filter(ref v) ? 1 : 0;
                }
                catch (Exception e)
                {
                    edi = ExceptionDispatchInfo.Capture(e);
                    return 1;
                }
            };

            IntPtr fp = Marshal.GetFunctionPointerForDelegate(f);
            SDL_FilterEvents(fp, IntPtr.Zero);

            if (edi != null)
                edi.Throw();
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        IEnumerator<Event> IEnumerable<Event>.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<Event>
        {
            Event ev;

            public Event Current => ev;
            object IEnumerator.Current => (object)Current;

            public bool MoveNext()
            {
                return SDL_PollEvent(out ev) != 0;
            }

            public void Reset() => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }
    }
}
