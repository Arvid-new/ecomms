#define demo_main
//#define demo_2

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ECOMMS_Client;
using ECOMMS_Entity;
using ECOMMS_Manager;
using ECOMMS_Participant;
using NATS.Client;

namespace ConsoleApp1
{
    /// <summary>
    /// example service particpant interface
    /// </summary>
    public interface IAddressServiceParticipant
    {
    }

    /// <summary>
    /// address service participant
    /// adds "nextaddress" request
    /// answers with a new, unique, address
    /// </summary>
    class AddressServiceParticipant : ServiceParticipant, IAddressServiceParticipant
    {
        public AddressServiceParticipant() : 
            base(Guid.Empty.ToString(), ECOMMS_Entity.Type.Address)
        {
        }

        public override void get(string what, Msg message)
        {
            switch (what)
            {
                case "nextaddress":
                    //get this through the instrument interface
                    replyTo(message, Guid.NewGuid().ToString());
                    break;
                default:
                    //not handled
                    base.get(what, message);
                    break;
            }
        }
    }

    /// <summary>
    /// example of how to create an instrument participant
    /// inherit from participant and override get, set, and action
    /// </summary>
    class SimInstrumentParticipant : InstrumentParticipant
    {
        public bool started { get; set; }

        public SimInstrumentParticipant(string address) : 
            base (address, ECOMMS_Entity.Type.Rheology)   //our sim is a rheology instrument
        {
            location = "nowhere";
            name = "sne";
            started = false;
        }

        public override void init()
        {
            base.init();

            Task.Factory.StartNew(() =>
            {
                //simulate a status
                while (true)
                {
                    Thread.Sleep(1000);
                    sendStatus("instrument.runstate", "Idle");

                    //try raising an event
                    raise("INSTRUMENT_RUNSTATE_CHANGED");
                }
            });

            //register action facility
            //you are expected to fulfill ack/nak/response protocol
            registerActionFacility("control.autosampler", (what, args) =>
            {
                switch(what)
                {
                    case "openlid":
                        Console.WriteLine("*** asked to openlid!!!");
                        ack(args.Message);
                        Console.WriteLine("*** opening!!!");
                        Thread.Sleep(5000);
                        Console.WriteLine("*** done opening!!!");
                        replyTo(args.Message, "SUCCESS");
                        break;
                }

            });
        }

        public override void get(string what, Msg message)
        {
            switch(what)
            {
                case "location":
                    //get this through the instrument interface
                    replyTo(message, location);
                    break;
                case "location.now":
                    //get this through the instrument interface
                    ack(message);
                    replyTo(message, location);
                    break;
                case "name":
                    //get this through the instrument interface
                    replyTo(message, name);
                    break;
                case "runstate":
                    replyTo(message, started.ToString());
                    break;
                default:
                    //not handled
                    base.get(what, message);
                    break;
            }
        }

        public override void set(string what, string payload, Msg message)
        {
            switch(what)
            {
                case "location":
                    //set through instrument interface
                    location = payload;

                    //always reply with status
                    replyTo(message, "success");
                    break;
                default:
                    //not handled
                    base.set(what, payload, message);
                    break;
            }
        }

        public override void action(string what, Msg message)
        {
            switch(what)
            {
                case "start":
                    //always reply with status
                    started = true;
                    replyTo(message, "success");
                    break;
                case "end":
                    //always reply with status
                    started = false;
                    replyTo(message, "success");
                    break;
                default:
                    base.action(what, message);
                    break;
            }
        }
    }

    /// <summary>
    /// example of how to create an instrument client
    /// </summary>
    class SimInstrumentClient : InstrumentClient
    {
        public SimInstrumentClient(string address) : 
            base(address, ECOMMS_Entity.Type.Thermal)
        {
        }

        bool _gotone = false;
        /// <summary>
        /// override to do something special in the instrument client
        /// when a status is received - user
        /// </summary>
        /// <param name="name"></param>
        /// <param name="data"></param>
        public override void statusReceived(string name, byte[] data)
        {
            //base class instrument client will notify of RUNSTATE_CHANGED
            //do something here that is required for this derived client
            //maybe look for states that are specific to the derived client
            //and notify
            base.statusReceived(name, data);

            if (name == "MY_SPECIAL_STATE")
                notify("MY_SPECIAL_STATE_CHANGED");
        }
    }

    class ClientFactory : IClientFactory
    {
        /// <summary>
        /// return a participant instance to the manager
        /// </summary>
        /// <param name="address"></param>
        /// <param name="role"></param>
        /// <param name="type"></param>
        /// <param name="subType"></param>
        /// <returns></returns>
        public IClient getClientFor(string address, Role role, ECOMMS_Entity.Type type, SubType subType)
        {
            IClient client = null;
            switch (role)
            {
                case Role.Instrument:
                    client = new SimInstrumentClient(address);    //new InstrumentClient(address, type);
                    break;

            }

            return client;
        }
    }
    class Program
    {
#if demo_main

        static void Main(string[] args)
        {
            //#region testing raise event with sim instrument participant
            //////instrument participant example - shim
            //var address = Guid.NewGuid().ToString();

            //SimInstrumentParticipant instrument = new SimInstrumentParticipant(address);
            //instrument.connect(@"nats://localhost:4222");
            //instrument.init();
            //#endregion

            #region create a manager
            Manager enterpriseManager = new Manager(new ClientFactory());

            enterpriseManager.connect(@"nats://192.168.86.31:7222");
            enterpriseManager.init();

            //observe the enterprise manager
            enterpriseManager.addObserver(new ObserverAdapter((o, h) =>
            {
                //need to wait to notify until after base class has gotton response
                //to role request
                //or have library query first before creating client
                //WIP...

                Thread.Sleep(1000);
                switch (h)
                {
                    case "CLIENTS_CHANGED":
                        Console.WriteLine(
                            "there are " +
                            enterpriseManager.clients.Where((i) => i.role == Role.Sensor).Count() +
                            " sensors online and " +
                            enterpriseManager.clients.Count() +
                            " clients"
                        );
                        break;
                }

            }));

            //addobserver(observerex) notifies with data which is the added client in this case
            enterpriseManager.addObserver(new ObserverAdapterEx((o, h, c) =>
            {
                //need to wait to notify until after base class has gotton response
                //to role request
                //or have library query first before creating client
                //WIP...

                var client = c as IClient;
                Thread.Sleep(1000);
                switch (h)
                {
                    case "CONNECTED":
                        //was it an instrument?
                        if(client.role == Role.Instrument)
                        {
                            Console.WriteLine(client.name + " INSTRUMENT CONNECTED");

                            //listen for run state changes
                            client.addObserver(new ObserverAdapterEx((anobject, ahint, data) =>
                            {
                                var bytes = (byte[])data;
                                var anInstrumentClient = (InstrumentClient)anobject;

                                if((ahint as string) == "RUNSTATE_CHANGED")
                                {
                                    var say = string.Format("{0} notified {1} with {2}",
                                        client.name,
                                        ahint,
                                        Encoding.UTF8.GetString(bytes, 0, bytes.Length)
                                        );

                                    Console.WriteLine(say);
                                }
                            }));

                            //add a status listener
                            client.addStatusListener((name, data) =>
                            {
                                Console.WriteLine("status listener: {0}", name);
                            });
                        }

                        if(client.role == Role.Sensor)
                        {

                            Console.WriteLine(client.name + " SENSOR CONNECTED");

                            //listen for run state changes
                            client.addObserver(new ObserverAdapterEx((anobject, ahint, data) =>
                            {
                                Console.WriteLine((ahint as string));
                            }));

                            //add a status listener
                            client.addStatusListener((name, bytes) =>
                            {
                                Console.WriteLine("status listener: {0}:{1}", name, Encoding.UTF8.GetString(bytes, 0, bytes.Length));
                            });
                        }
                        break;
                }

            }));

            #endregion

            //#region create instrument participant and client
            ////instrument participant example - shim
            //var address = Guid.NewGuid().ToString();

            //SimInstrumentParticipant instrument = new SimInstrumentParticipant(address);
            //instrument.connect(@"nats://localhost:4222");
            //instrument.init();

            //////create a client and an instrument that are looking
            //////at the same participant ( the shim side and the client side )
            ////InstrumentClient client = new InstrumentClient(address);
            ////client.connect(@"nats://localhost:4222");
            ////client.init();

            //////observe the client
            ////client.addObserver(new ObserverAdapter((observable, hint) =>
            ////{
            ////    switch (hint)
            ////    {
            ////        case "STATUS_RECEIVED":
            ////            Console.WriteLine("notfied through observer");
            ////            break;
            ////    }
            ////}));

            //#endregion

            Console.WriteLine("START SHIM IF IT IS NOT ALREADY RUNNING...");

        }
#endif

#if demo_2
        static void Main(string[] args)
        {
        #region create a manager
            Manager enterpriseManager = new Manager();
            enterpriseManager.connect();
            enterpriseManager.init();
        #endregion

        #region create address service participant and client
            //start up an address service
            AddressServiceParticipant asp = new AddressServiceParticipant();

            //connect returns false if the server is not running 
            //may change this to allow the connection exception to leak
            //into the client code
            //this will necessarily change when we move to cluster servers
            //or provide server redundancy
            asp.connect(@"nats://localhost:4222"); //<<<< send nats ip or leave empty for localhost:4222
            asp.init();

            //create a client to talk to the address service
            var aspClient = new Client(Guid.Empty.ToString(), Role.Service, ECOMMS_Entity.Type.Address);
            aspClient.connect(@"nats://localhost:4222");
            aspClient.init();

            //blocking request to get address from service client
            //to be used to create instrument participant and client below
            var address = aspClient.request("get", "nextaddress");
        #endregion

        #region create instrument participant and client
            //instrument participant example - shim
            SimInstrumentParticipant instrument = new SimInstrumentParticipant(address);
            instrument.connect(@"nats://localhost:4222");
            instrument.init();

            //create a client and an instrument that are looking
            //at the same participant ( the shim side and the client side )
            SimInstrumentClient client = new SimInstrumentClient(address);
            client.connect(@"nats://localhost:4222");
            client.init();

            //observe the client
            client.addObserver(new ObserverAdapter((observable, hint)=> 
            {
                switch(hint)
                {
                    case "STATUS_RECEIVED":
                        Console.WriteLine("notfied through observer");
                        break;
                }
            }));

        #endregion

            //call through client with bogus get request
            //using new protocol request ( ack/nak/response )
            //this will be wrapped by the doGet() method but is available
            //in the base class Entity
            client.protocolRequest("get", "fliplikewilson", new ResponseCompletionAdapter(
                ()      =>  {
                    Console.WriteLine("ResponeCompletionAdapter ack");
                },
                (error) =>  {
                    Console.WriteLine("ResponseCompletionAdapter [get][fliplikewilson] : DANGER WILL ROBINSON!");
                },
                (bytes) =>  {
                }
            ));

            //call through client with valid get request
            //ADD NAMED PARAMETERS FOR MORE CLARITY...
            client.protocolRequest("get", "location", new ResponseCompletionAdapter(
                ack:() => {
                    Console.WriteLine("ResponeCompletionAdapter ack");
                },
                nak:(error) => {
                    Console.WriteLine("ResponseCompletionAdapter [get][location] : DANGER WILL ROBINSON!");
                },
                response:(bytes) => {
                    Console.WriteLine("ResponseCompletionAdapter [get][location] : " + Encoding.UTF8.GetString(bytes, 0, bytes.Length));
                }
            ));

            //call through client with valid get request
            //ADD NAMED PARAMETERS FOR MORE CLARITY...
            //ONLY PROVIDE RESPONSE CALLBACK
            client.protocolRequest("get", "location.now", new ResponseCompletionAdapter(
                response: (bytes) => {
                    Console.WriteLine("ResponseCompletionAdapter [get][location.now] : " + Encoding.UTF8.GetString(bytes, 0, bytes.Length));
                },
                ack: () => {
                    Console.WriteLine("ResponseCompletionAdapter [ack][location.now] : ");
                }
            ));

            //call through Client doGet method
            client.doGet("location", (s) =>
            {
                Console.WriteLine("the instrument client location is " + s);
            });

            //call demo address service get
            aspClient.doGet("nextaddress", (s) =>
            {
                Console.WriteLine("address service has returned " + s);
            });

            client.doGet("location", (s) =>
            {
                Console.WriteLine("the instrument client location is " + s);
            });

            client.doSet("location", "somewhere", (s) =>
            {
                Console.WriteLine("location set was a " + s);
            });

            client.doAction("start", (s) =>
            {
                Console.WriteLine("sent START action to instrument and it was a " + s);

                Console.WriteLine("showing instrument client properties:");
                Console.WriteLine("location: " + instrument.location);
                Console.WriteLine("started flag: " + instrument.started);
            });

            //through registered facility - action
            //client.doAction("control.autosampler.openlid", (s) =>
            //{
            //});

            //through registered facility - IResponseCompletionAdapter
            client.doAction("control.autosampler.openlid", new ResponseCompletionAdapter(
                ack: () => Console.WriteLine("control.autosampler.openlid got ACK"),
                response: (bytes) => Console.WriteLine("control.autosampler.openlid got RESPONSE " + Encoding.UTF8.GetString(bytes, 0, bytes.Length))
            ));

            //through normal path with completion adapter
            client.doAction("openlid", new ResponseCompletionAdapter(
                ack: () => Console.WriteLine("control.autosampler.openlid got ACK"),
                response: (bytes) => Console.WriteLine("control.autosampler.openlid got RESPONSE " + Encoding.UTF8.GetString(bytes, 0, bytes.Length))
            ));


            Thread.Sleep(1000);

            client.doAction("end", (s) =>
            {
                Console.WriteLine("sent END action to instrument and it was a " + s);

                Console.WriteLine("showing instrument client properties:");
                Console.WriteLine("location: " + instrument.location);
                Console.WriteLine("started flag: " + instrument.started);
            });

            Thread.Sleep(5000);

            Console.WriteLine(enterpriseManager.clients.Count);

            Console.WriteLine(
                "there are " +
                enterpriseManager.clients.Where((i) => i.role == Role.Instrument).Count() +
                " instruments online"
            );

            //observe the enterprise manager
            enterpriseManager.addObserver(new ObserverAdapter((o, h) =>
            {
                //need to wait to notify until after base class has gotton responses
                //or have library query first before creating client

                Thread.Sleep(1000);
                switch(h)
                {
                    case "CLIENTS_CHANGED":
                        Console.WriteLine(
                            "there are " +
                            enterpriseManager.clients.Where((i) => i.role == Role.Instrument).Count() +
                            " instruments online"
                        );
                        break;
                }

            }));

            Console.WriteLine("START ANOTHER CONSOLE APP...");

            Console.ReadKey();

        }
#endif
    }
}
