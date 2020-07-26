using ECOMMS_Entity;
using NATS.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;

namespace ECOMMS_Client
{
    public interface IClient : IEntity
    {
        void init();
        void doGet(string what, Action<string> callback);
        void doGet(string what, IResponseCompletion callbacks);
        void doSet(string what, string with, Action<string> callback);
        void doSet(string what, byte[] with, Action<string> callback);
        void doAction(string what, Action<string> callback);
        void doAction(string what, IResponseCompletion callbacks);
        void statusReceived(string name, byte[] data);
        void addStatusListener(Action<string, byte[]> listener);
        void addStatusListener(object key, Action<string, byte[]> listener);
        void removeListener(object key);
        void removeAllListeners();

        //indicate online/offline by listening to heartbeat
        bool online { get; set; }
        //void on(string anEventString, Action<string> callback);
    }

    public interface IInstrumentClient : IClient
    {
        //empty for now
    }

    public interface IServiceClient : IClient
    {
        //empty for now
    }
        
    /// <summary>
    /// client behavior
    /// define client side get/set/action/status behavior
    /// </summary>
    public class Client : Entity, IClient
    {
        bool _online = true;
        List<Action<string, byte[]>> _statusListners = new List<Action<string, byte[]>>();

        bool _isInitialized = false;
        int _propertiesInitialized = 0;
        int _propertiesInitializedTarget = 1 | 2 | 4 | 8;
        private void propertyInitialized(int which)
        {
            lock(this)
            {
                _propertiesInitialized |= which;
                if (!_isInitialized)
                {
                    if (_propertiesInitializedTarget == _propertiesInitialized)
                    {
                        _isInitialized = true;
                        notify("INITIALIZED");
                    }
                }
            }
        }

        public bool online
        {
            get
            {
                return _online;
            }
            set
            {
                lock(this)
                {
                    if (value != _online)
                    {
                        _online = value;
                        notify("ONLINE_CHANGED");
                    }

                    _online = value;
                }
            }
        }
        public Client(
            string id,
            Role role,
            ECOMMS_Entity.Type type) :
            base(id, role, type)
        {
            name = "NONAME"; //will fetch from participant
        }

        Timer _hb_timer = new Timer();

        /// <summary>
        /// request role and type
        /// </summary>
        public override void init()
        {
            //get the partipants role
            request("get", "name", (s) =>
            {
                name = s;
                propertyInitialized(1);
            });

            //get the partipants role
            request("get", "role", (s)=> 
            {
                switch(s)
                {
                    case "Instrument":
                        role = Role.Instrument;
                        break;
                    case "Service":
                        role = Role.Service;
                        break;
                    case "Equipment":
                        role = Role.Equipment;
                        break;
                    case "Sensor":
                        role = Role.Sensor;
                        break;
                    default:
                        role = Role.None;
                        break;
                }

                propertyInitialized(2);
            });

            request("get", "type", (s) =>
            {
                switch (s)
                {
                    case "Rheology":
                        type = ECOMMS_Entity.Type.Rheology;
                        break;
                    case "Thermal":
                        type = ECOMMS_Entity.Type.Thermal;
                        break;
                    case "PersistentStore":
                        type = ECOMMS_Entity.Type.PersistentStore;
                        break;
                    case "Address":
                        type = ECOMMS_Entity.Type.Address;
                        break;
                    case "Balance":
                        type = ECOMMS_Entity.Type.Balance;
                        break;
                    case "Calculator":
                        type = ECOMMS_Entity.Type.Calculator;
                        break;
                    case "Temperature":
                        type = ECOMMS_Entity.Type.Temperature;
                        break;
                }

                propertyInitialized(4);
            });

            request("get", "subtype", (s) =>
            {
                switch (s)
                {
                    default:
                        try
                        {
                            subType = (SubType)Enum.Parse(typeof(SubType), s);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                        break;
                }

                propertyInitialized(8);
            });

            //get the participantw type

            //example of how to use protocol request to get role
            //protocolRequest("get", "role", new ResponseCompletionAdapter
            //{
            //    ack =           ()      => { Console.WriteLine("ack");  },
            //    nak =           (error) => { },
            //    response =      (bytes) => { Console.WriteLine("response"); }
            //});

            //listen for status
            register("status", (s, a) => 
            {
                notify("STATUS_RECEIVED");

                //ask derived class,  handled?
                statusReceived("", a.Message.Data);
            });

            register("status.>", (s, a) =>
            {
                //sledgehammer approach for now
                //get subject, lopp off id, lop off "status"
                var subject = a.Message.Subject;
                var noid = subject.Substring(subject.IndexOf('.') + 1);
                var name = noid.Substring(noid.IndexOf('.') + 1);

                statusReceived(name, a.Message.Data);
            });

            //notify on event
            register("event", (s, a) => {
                var bytes = a.Message.Data;
                var data = new byte[0];

                //call through new interface with no data
                notify(Encoding.UTF8.GetString(bytes, 0, bytes.Length), data);
            });

            var watch = System.Diagnostics.Stopwatch.StartNew();
            System.Timers.Timer _hb_timer = new System.Timers.Timer();

            Msg _m_inflight = null;
            _hb_timer.Interval = 3000;

            _hb_timer.Elapsed += (s,a) => 
            {
                if (watch.ElapsedMilliseconds > 4500)
                {
                    if(_m_inflight != null)
                    {
                        _m_inflight.ArrivalSubcription.Unsubscribe();
                        _m_inflight = null;
                    }

                    online = false;                    
                }
            };

            _hb_timer.Start();

            //keep track of my heartbeat - advertisement
            //could be implemented in terms of observer
            registerHeartbeatListener((s, a) =>
            {
                _m_inflight = a.Message;
                string heartbeat = Encoding.UTF8.GetString(a.Message.Data, 0, a.Message.Data.Length);

                if (heartbeat == id)
                {
                    //heartbeats are coming every 3 seconds
                    //go offline if we dont see one in 4.5

#if DEBUG
                    Console.WriteLine(heartbeat + ":" + watch.ElapsedMilliseconds);
#endif
                    if (watch.ElapsedMilliseconds > 4500)
                    {
#if DEBUG
                        Console.WriteLine("offline");
#endif
                        online = false;
                    }
                    else
                    {
                        online = true;
                    }

                    watch.Restart();
                }
            });
        }

        public virtual void statusReceived(string name, byte[] data)
        {
            Action<string, byte[]>[] listeners = new Action<string, byte[]>[_statusListners.Count];

            _statusListners.CopyTo(listeners, 0);
            foreach (Action<string, byte[]> listener in listeners)
                listener(name, data);

            //make sure that overrides call through base class
            //foreach (var listener in _statusListners)
            //    listener(name, data);
        }

        public void doGet(string what, Action<string> callback)
        {
            request("get", what, callback);
        }

        public void doGet(string what, IResponseCompletion callbacks)
        {
            request("get", Encoding.UTF8.GetBytes(what), callbacks);
        }

        public void doSet(string what, string with, Action<string> callback)
        {
            request("set." + what, with, callback);
        }

        public void doSet(string what, byte[] with, Action<string> callback)
        {
            request("set." + what, with, callback);
        }

        public void doAction(string what,  Action<string> callback)
        {
            request("action", what, callback);
        }
        
        public void doAction(string what, IResponseCompletion callbacks)
        {
            request("action." + what, Encoding.UTF8.GetBytes(what), callbacks);
        }

        public void addStatusListener(Action<string, byte[]> listener)
        {
            _statusListners.Add(listener);
        }

        Dictionary<object, Action<string, byte[]>> _boundListeners = new Dictionary<object, Action<string, byte[]>>();
        public void addStatusListener(object key, Action<string, byte[]> listener)
        {
            _boundListeners[key] = listener;
            _statusListners.Add(listener);
        }

        public void removeListener(object key)
        {
            Action<string, byte[]> listener = _boundListeners[key];
            _statusListners.Remove(listener);
        }

        public void removeAllListeners()
        {
            _statusListners.Clear();
        }
    }

    /// <summary>
    /// define some common instrument client behavior
    /// nop for now
    /// </summary>
    public class InstrumentClient : Client, IInstrumentClient
    {
        //nop for now
        //provide instrument common client behvavior here
        public InstrumentClient(string id, ECOMMS_Entity.Type type) : 
            base(id, Role.Instrument, type)
        {
        }

        public override void statusReceived(string name, byte[] data)
        {
            base.statusReceived(name, data);

            //if run state then notify runstate changed
            if (name == "runstate")
                notify("RUNSTATE_CHANGED", data);
        }
    }

    public class ServiceClient : Client, IServiceClient
    {
        //nop for now
        //provide service common client behvavior here
        public ServiceClient(string id) :
            base(id, Role.Service, ECOMMS_Entity.Type.None)
        {
        }
    }

}
