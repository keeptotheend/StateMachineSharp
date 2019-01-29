using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace StateMachineSharp
{
    public class Erros
    {
        public string err;
        public int ec;
        public Erros(string err, int ec)
        {
            this.err = err;
            this.ec = ec;
        }
    }

    public class Event
    {
        public StateMachine fsm;
        public string et; // event
        public string dst;
        public string src;
        public Erros err;
        public object[] args;
        public bool canceled;
        public bool async;

        public void Cancel(Erros err)
        {
            this.canceled = true;
            this.err = err;
        }

        public void Async()
        {
            this.async = true;
        }
    }

    public class EventDesc
    {
        public string name;
        public List<string> src = new List<string>();
        public string dst;
    }
    

    public delegate int Callback (Event e);
    public delegate void Func ();

    public enum CallBackType
    {
        callbackNone,
        callbackBeforeEvent,
        callbackLeaveState,
        callbackEnterState,
        callbackAfterEvent
    }

    public class cKey
    {
        public string target;
        public CallBackType callbackType;
        public cKey(string target, CallBackType callbackType)
        {
            this.target = target;
            this.callbackType = callbackType;
        }
    }

    public class eKey
    {
        public string et;
        public string src;
        public eKey(string et, string src)
        {
            this.et = et;
            this.src = src;
        }
    }

    public class Transitioner
    {
        public virtual Erros transition(StateMachine fsm)
        {
            if(fsm.transition == null)
            {
                return new Erros("NotInTransitionError", 3);
            }
            fsm.transition.Done();
            fsm.transition = null;
            return null;
        }
    }

    public class StateMachine
    {
        public class StateMachineTransition{
            public string current;
            public Event e;
            public StateMachine fsm;
            public void Done()
            {
                this.fsm.EnterStateCallbacks(this.e);
                this.fsm.AfterEventCallbacks(this.e);
            }
        };

        public string current;
        public Dictionary<eKey, string> transitions;
        public Dictionary<cKey, Callback> callbacks;
        public StateMachineTransition transition;

        public Transitioner transitionerObj;

	    // // stateMu guards access to the current state.
        // Mutex stateMu = new Mutex();
        // // eventMu guards access to Event() and Transition().
        // Mutex eventMu = new Mutex();

        public string Current()
        {
            return this.current;
        }

        public bool Is(string state) 
        {
            return state == this.current;
        }
        public void SetState(string state) 
        {
	        this.current = state;
        }

        public bool Can(string et)
        {
            string target = "";
            bool ok = this.transitions.TryGetValue(new eKey(et, this.current), out target);
            return ok && (this.transition == null);
        }
        public bool Cannot(string et)
        {
            return !Can(et);
        }

        public List<string> AvailableTransitions()
        {
            List<string> transitions = new List<string>();
            foreach(var t in this.transitions) 
            {
                if(t.Key.src == this.current)
                {
                    transitions.Add(t.Key.et);
                }
            }
            return transitions;           
        }

        public Erros Event(string et, params object[] args)
        {
            if(this.transition != null)
            {
                return new Erros("InTransitionError", 4);
            }
            string dst = "";
            if(this.transitions.TryGetValue(new eKey(et, this.current), out dst) == false)
            {
                foreach(var ekey in this.transitions)
                {
                    if(ekey.Key.et == et)
                    {
                        return new Erros("InvalidEventError", 0);
                    }
                }
                return new Erros("UnknownEventError", 5);
            }
            
            var e = new Event();
            e.fsm = this;
            e.src = this.current;
            e.dst = dst;
            e.args = args;
            e.canceled = false;
            e.async = false;

            var err = this.BeforeEventCallbacks(e);
            if(err != null)
            {
                return err;
            }

            if(this.current == dst)
            {
                this.AfterEventCallbacks(e);
                return new Erros("NoTransitionError", 4);
            }

            // Setup the transition, call it later.
            this.transition = new StateMachineTransition();
            this.transition.current = this.current;
            this.transition.fsm = this;
            this.transition.e = e;

            err = this.LeaveStateCallbacks(e);
            if(err != null)
            {
                if(err.ec == 1)
                {
                    this.transition = null;
                }
                return err;
            }
            
            err = this.doTransition();
            if(e != null)
            {
                return new Erros("InternalError", 6);
            }
            return null;
        }
        
        private Erros Transition()
        {
            return this.doTransition();
        }

        private Erros doTransition()
        {
            return this.transitionerObj.transition(this);
        }
        public Erros BeforeEventCallbacks(Event e)
        {
            Callback cb;
            if(this.callbacks.TryGetValue(new cKey(e.et, CallBackType.callbackBeforeEvent), out cb) == false)
            {
                cb(e);
                if(e.canceled)
                {
                    return new Erros("CanceledError", 1);
                }
            }
            if(this.callbacks.TryGetValue(new cKey("", CallBackType.callbackBeforeEvent), out cb) == false)
            {
                cb(e);
                if(e.canceled)
                {
                    return new Erros("CanceledError", 1);
                }
            }              
            return null;
        }

        public Erros LeaveStateCallbacks(Event e)
        {
            Callback cb;
            if(this.callbacks.TryGetValue(new cKey(this.current, CallBackType.callbackLeaveState), out cb) == false)
            {
                cb(e);
                if(e.canceled)
                {
                    return new Erros("CanceledError", 1);
                }
                else if(e.async)
                {
                    return new Erros("AsyncError", 2); 
                }
            }
            if(this.callbacks.TryGetValue(new cKey("", CallBackType.callbackLeaveState), out cb) == false)
            {
                cb(e);
                if(e.canceled)
                {
                    return new Erros("CanceledError", 1);
                }
                else if(e.async)
                {
                    return new Erros("AsyncError", 2); 
                }
            }              
            return null;
        }


        public Erros EnterStateCallbacks(Event e)
        {
            Callback cb;
            if(this.callbacks.TryGetValue(new cKey(this.current, CallBackType.callbackEnterState), out cb) == false)
            {
                cb(e);
            }
            if(this.callbacks.TryGetValue(new cKey("", CallBackType.callbackEnterState), out cb) == false)
            {
                cb(e);
            }
            return null;
        }

        public Erros AfterEventCallbacks(Event e)
        {
            Callback cb;
            if(this.callbacks.TryGetValue(new cKey(e.et, CallBackType.callbackAfterEvent), out cb) == false)
            {
                cb(e);
            }
            if(this.callbacks.TryGetValue(new cKey("", CallBackType.callbackAfterEvent), out cb) == false)
            {
                cb(e);
            }
            return null;
        }
    }

    public class StateMachineFactory
    {
        public static string VERSION = "2.3.5";

        static StateMachine create(string initial, List<EventDesc> events, Dictionary<string, Callback> callbacks)
        {
            StateMachine fsm = new StateMachine();
            fsm.transitionerObj = new Transitioner();
            fsm.current = initial;
            fsm.transitions = new Dictionary<eKey, string>();
            fsm.callbacks = new Dictionary<cKey, Callback>();

            var allEvents = new Dictionary<string, bool>();
            var allStates = new Dictionary<string, bool>();
            foreach(var e in events) 
            {
                foreach(var s in e.src) 
                {
                    fsm.transitions[new eKey(e.name, s)] = e.dst;
                    allStates[s] = true;
                    allStates[e.dst] = true;
                }
                allEvents[e.name] = true;
            }

            // Map all callbacks to events/states.
            foreach (var cb in callbacks) {
                var name = cb.Key;

                string target = "";
                CallBackType callbackType = CallBackType.callbackNone;
                
                if (name.StartsWith("before_")) 
                {
                    target = name.TrimStart("before_".ToCharArray());
                    if(target == "event")
                    {
                        target = "";
                        callbackType = CallBackType.callbackBeforeEvent;
                    }
                    else
                    {
                        bool ok = false;
                        if(allEvents.TryGetValue(target, out ok))
                        {
                            callbackType = CallBackType.callbackBeforeEvent;
                        }
                    }
                }
                else if(name.StartsWith("leave_"))
                {
                    target = name.TrimStart("leave_".ToCharArray());
                    if(target == "state")
                    {
                        target = "";
                        callbackType = CallBackType.callbackLeaveState;
                    }
                    else
                    {
                        bool ok = false;
                        if(allStates.TryGetValue(target, out ok))
                        {
                            callbackType = CallBackType.callbackLeaveState;
                        }
                    }
                }
                else if(name.StartsWith("enter_"))
                {
                    target = name.TrimStart("enter_".ToCharArray());
                    if(target == "state")
                    {
                        target = "";
                        callbackType = CallBackType.callbackEnterState;
                    }
                    else
                    {
                        bool ok = false;
                        if(allStates.TryGetValue(target, out ok))
                        {
                            callbackType = CallBackType.callbackEnterState;
                        }
                    }                    
                }
                else if(name.StartsWith("after_"))
                {
                    target = name.TrimStart("after_".ToCharArray());
                    if(target == "event")
                    {
                        target = "";
                        callbackType = CallBackType.callbackAfterEvent;
                    }
                    else
                    {
                        bool ok = false;
                        if(allEvents.TryGetValue(target, out ok))
                        {
                            callbackType = CallBackType.callbackAfterEvent;
                        }
                    }                    
                }
                else
                {
                    target = name;
                    bool ok = false;
                    if(allStates.TryGetValue(target, out ok))
                    {
                        callbackType = CallBackType.callbackEnterState;
                    }
                    if(allEvents.TryGetValue(target, out ok))
                    {
                        callbackType = CallBackType.callbackAfterEvent;
                    }
                }

                if(callbackType != CallBackType.callbackNone)
                {
                    fsm.callbacks[new cKey(target, callbackType)] = cb.Value;
                }
            }            

            return fsm;
        }
    }
}

