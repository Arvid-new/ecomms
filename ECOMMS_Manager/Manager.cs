using ECOMMS_Client;
using ECOMMS_Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace ECOMMS_Manager
{
    public interface IClientFactory
    {
        IClient getClientFor(
            string address, 
            Role role, 
            ECOMMS_Entity.Type type,
            SubType subType);
    }

    public class ManagerClient : Client
    {
        public ManagerClient(string id, Role role, ECOMMS_Entity.Type type) : base(id, role, type) { }
    }

    public interface IManager
    {
    }

    public class Manager : Entity, IManager
    {
        IList<IClient> _clients = new List<IClient>();
        IClientFactory _clientFactory;

        public IList<IClient> clients { get { return _clients; } }
        public Manager(IClientFactory factory = null) : base("", Role.Service, ECOMMS_Entity.Type.Address)
        {
            _clientFactory = factory;
        }

        List<IClient> _tempClients = new List<IClient>();
        public override void init()
        {
            base.init();

            registerHeartbeatListener((s, a) =>
            {
                string heartbeat = Encoding.UTF8.GetString(a.Message.Data, 0, a.Message.Data.Length);

                var manager_found_list = _tempClients.Where((participant) => participant.id.Equals(heartbeat)).ToList();
                var found_list = _clients.Where((participant) => participant.id.Equals(heartbeat)).ToList();

                //we dont have a client for the heartbeat we just saw
                //so create one
                if (found_list.Count == 0 && manager_found_list.Count == 0)
                {
                    IClient client;

                    //CREATE A CLIENT FOR THIS HEARTBEAT
                    //WAIT FOR IT TO BE INITIALIZED TO PUT IT INTO LIST
                    //AS ITS ROLE IS NOT KNOWN UNTIL THEN

                    client = new ManagerClient(heartbeat, Role.None, ECOMMS_Entity.Type.None);
                    client.connect(server);
                    client.init();

                    _tempClients.Add(client);

                    Manager manager = this;

                    client.addObserver(new ObserverAdapter((observable, h) =>
                    {
                        Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!{0}", observable.GetType());
                        var observedClient = (IClient)observable;
                        switch (h as string)
                        {
                            case "INITIALIZED":
                                if(observable is ManagerClient)
                                {
                                    if (_clientFactory != null)
                                    {
                                        IClient factoryClient = _clientFactory.getClientFor(
                                            heartbeat,
                                            observedClient.role,
                                            observedClient.type,
                                            observedClient.subType);

                                        if (factoryClient != null)
                                        {
                                            factoryClient.connect(server);
                                            factoryClient.init();

                                            factoryClient.addObserver(new ObserverAdapter((oo, hh) =>
                                            {
                                                switch (hh as string)
                                                {
                                                    case "INITIALIZED":
                                                        _clients.Add(oo as IClient);
                                                        manager.notify("CONNECTED", oo);
                                                        break;
                                                    case "ONLINE_CHANGED":
                                                        if (!(oo as IClient).online)
                                                        {
                                                            manager.notify("DISCONNECTED", oo);

                                                            _clients.Remove(oo as IClient);
                                                            manager.notify("CLIENTS_CHANGED");

                                                            (oo as IClient).removeAllObservers();
                                                            (oo as IClient).removeAllListeners();
                                                        }
                                                        break;
                                                }
                                            }));

                                            }
                                        else
                                        {
                                            _clients.Add(observedClient);
                                            manager.notify("CONNECTED", observedClient);
                                        }
                                    }
                                    else
                                    {
                                        _clients.Add(observedClient);
                                        manager.notify("CONNECTED", observedClient);
                                    }
                                }
                                else if(!_clients.Contains(observedClient))
                                {
                                    _clients.Add(observedClient);
                                    manager.notify("CONNECTED", observedClient);
                                }

                                break;

                            case "ONLINE_CHANGED":
                                if (!client.online)
                                {
                                    //manager.notify("DISCONNECTED", observedClient);

                                    _clients.Remove(observedClient);
                                    _tempClients.Remove(observedClient);

                                    //manager.notify("CLIENTS_CHANGED");

                                    observedClient.removeAllObservers();
                                    observedClient.removeAllListeners();
                                }
                                break;
                        }
                    }));
                }
            });
        }

        public void initX()
        {
            base.init();

            registerHeartbeatListener((s, a) =>
            {
                string heartbeat = Encoding.UTF8.GetString(a.Message.Data, 0, a.Message.Data.Length);

                var found_list = _clients.Where((participant) => participant.id.Equals(heartbeat)).ToList();

                //we dont have a client for the heartbeat we just saw
                //so create one
                if (found_list.Count == 0)
                {
                    IClient client;

                    //create a client for this participant and add to list
                    client = new Client(heartbeat, Role.None, ECOMMS_Entity.Type.None);
                    client.connect(server);
                    client.init();

                    //use this base client to find out the role of the client that
                    //just connected

                    //if its an instrument then create an instrument client
                    //and add it
                    //if we have a factory instance then use it otherwise
                    //create a base class entity

                    Manager manager = this;

                    //_clients.Add(client);

                    client.addObserver(new ObserverAdapter((o, h) =>
                    {
                        switch(h as string)
                        {
                            case "INITIALIZED":
                                if (client.role == Role.Instrument && _clientFactory == null)
                                {
                                    var instrumentClient = new InstrumentClient(heartbeat, client.type);
                                    instrumentClient.connect(server);
                                    instrumentClient.init();

                                    //_clients.Remove(client);

                                    client = instrumentClient;

                                    _clients.Add(client);
                                }
                                else if (_clientFactory != null)
                                {
                                    IClient tempClient = _clientFactory.getClientFor(heartbeat, client.role, client.type, SubType.None);
                                    if (tempClient != null)
                                    {
                                        tempClient.connect(server);
                                        tempClient.init();

                                        //_clients.Remove(client);

                                        client = tempClient;

                                        _clients.Add(client);
                                    }
                                }

                                //manager.notify("CLIENTS_CHANGED");
                                manager.notify("CONNECTED", client);

                                break;

                            case "ONLINE_CHANGED":
                                if (!client.online)
                                {
                                    manager.notify("DISCONNECTED", client);

                                    _clients.Remove(client);
                                    manager.notify("CLIENTS_CHANGED");

                                    client.removeAllObservers();
                                    client.removeAllListeners();
                                }
                                break;
                        }
                    }));
                }
            });
        }
    }
}
