using ECOMMS_Entity;
using NATS.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ECOMMS_Participant
{
    public interface IParticipant : IEntity
    {
        /// <summary>
        /// perform get
        /// </summary>
        /// <param name="what">get what</param>
        /// <param name="message">the message</param>
        /// <returns>true is handled,  call reply</returns>
        void get(string what, Msg message);
        void set(string what, string with, Msg message);
        void action(string what, Msg message);
        void sendStatus(string data);
        void sendStatus(string kind, string data);
        void sendStatus(string kind, byte[] data);
        void replyTo(Msg message, string answer);
        void replyTo(Msg message, byte[] response);
        void ack(Msg message);
        void nak(Msg message);
        void registerActionFacility(string facility, Action<string, MsgHandlerEventArgs> callback);
        /// <summary>
        /// inform clients of a state change
        /// raise an event
        /// </summary>
        /// <param name="anEventString"></param>
        void raise(string anEventString);
    }

    public class Participant : Entity, IParticipant
    {
        Dictionary<string, Action<string, MsgHandlerEventArgs>> 
            _facilityActions = new Dictionary<string, Action<string, MsgHandlerEventArgs>>();

        public Participant(string id, Role role, ECOMMS_Entity.Type type):
            base(id, role, type)
        {
            //start with role as name,  expect derived class to set
            //decide if we want to pass in constructor
            name = role.ToString(); 
        }

        public void registerActionFacility(string facility, Action<string, MsgHandlerEventArgs> callback)
        {
            _facilityActions.Add(facility, callback);
        }

        public virtual void get(string what, Msg message)
        {
            switch (what)
            {
                case "name":
                    //support both kinds of interactions for now
                    if (message.Reply.Contains("TA_MBOX"))
                    {
                        ack(message);
                        replyTo(message, name);
                    }
                    else
                    {
                        replyTo(message, name);
                    }
                    break;
                case "role":
                    //support both kinds of interactions for now
                    if (message.Reply.Contains("TA_MBOX"))
                    {
                        ack(message);
                        replyTo(message, role.ToString());
                    }
                    else
                    {
                        replyTo(message, role.ToString());
                    }
                    break;
                case "type":
                    //support both kinds of interactions for now
                    if (message.Reply.Contains("TA_MBOX"))
                    {
                        ack(message);
                        replyTo(message, type.ToString());
                    }
                    else
                    {
                        replyTo(message, type.ToString());
                    }
                    break;
                case "subtype":
                    //support both kinds of interactions for now
                    if (message.Reply.Contains("TA_MBOX"))
                    {
                        ack(message);
                        replyTo(message, subType.ToString());
                    }
                    else
                    {
                        replyTo(message, subType.ToString());
                    }
                    break;
                default:
                    //need to decide how naks are transmitted
                    //as this is not enough to know why we nak-ed
                    nak(message);
                    break;
            }
        }

        public virtual void set(string what, string payload, Msg message)
        {
            //reply here with a nak if these move to ack/nak/response
        }

        public virtual void action(string what, Msg message)
        {
            //reply here with a nak
        }

        public void replyTo(Msg message, string payload)
        {
            publish(message.Reply, payload);
        }

        public void replyTo(Msg message, byte[] payload)
        {
            publish(message.Reply, payload);
        }

        /// <summary>
        /// raise an event 
        /// </summary>
        /// <param name="anEventString"></param>
        public void raise(string anEventString)
        {
            publish(id + ".event", anEventString);
        }

        /// <summary>
        /// implement ack protocol
        /// ack the passed message - publish our ack to the reply ta temporary mailbox
        /// </summary>
        /// <param name="message"></param>
        public void ack(Msg message)
        {
            replyTo(message, "ACK");
        }

        /// <summary>
        /// implement ack protocol
        /// nak the passed message - publish our ack to the reply ta temporary mailbox
        /// </summary>
        /// <param name="message"></param>
        public void nak(Msg message)
        {
            replyTo(message, "NAK");
        }

        /// <summary>
        /// send status as me
        /// </summary>
        /// <param name="data"></param>
        public void sendStatus(string data)
        {
            publish(id + ".status", data);
        }

        /// <summary>
        /// send status as me
        /// </summary>
        /// <param name="kind"></param>
        /// <param name="data"></param>
        public void sendStatus(string kind, string data)
        {
            publish(id + ".status." + kind, data);
        }

        public void sendStatus(string kind, byte[] data)
        {
            publish(id + ".status." + kind, data);
        }

        /// <summary>
        /// initialize partipant
        /// start sending heatbeat
        /// </summary>
        public override void init()
        {
            //register common topics

            //listen for common gets
            register("get", (s, a) =>
            {
                string what = Encoding.UTF8.GetString(a.Message.Data, 0, a.Message.Data.Length);

                get(what, a.Message);
            });

            //listen for common sets
            register("set.>", (s, a) =>
            {
                //asumes a subject that looks like this for now:
                //<guid>.set.<what>
                var subject = a.Message.Subject;
                var tokens = subject.Split('.');
                string what = tokens[2];

                var with = Encoding.UTF8.GetString(a.Message.Data, 0, a.Message.Data.Length);

                set(what, with, a.Message);
            });

            //this happens on thread provided by nats client library
            register("action", (s, a) => 
            {
                string what = Encoding.UTF8.GetString(a.Message.Data, 0, a.Message.Data.Length);

                action(what, a.Message);

                //determine if a facility is present ie. a dotted payload
                //consider: 
                //30dd879c-ee2f-11db-8314-0800200c9a66.action.control.autosampler.openlid
                //what is: control.autosampler.openlid
                //look for facilityAction: control.autosampler
                //call callback:  callback("openlid");
                //
                var tokens = what.Split('.');
                if(tokens.Length > 1)
                {
                    string facility = what.Substring(0, what.LastIndexOf('.'));
                    Action<string, MsgHandlerEventArgs> callback;
                    if (_facilityActions.TryGetValue(facility, out callback))
                    {
                        callback(what.Substring(what.LastIndexOf('.') + 1), a);
                    }
                }

            });

            //this happens on thread provided by nats client library
            register("action.>", (s, a) =>
            {
                string what = Encoding.UTF8.GetString(a.Message.Data, 0, a.Message.Data.Length);

                action(what, a.Message);

                //determine if a facility is present ie. a dotted payload
                //consider: 
                //30dd879c-ee2f-11db-8314-0800200c9a66.action.control.autosampler.openlid
                //what is: control.autosampler.openlid
                //look for facilityAction: control.autosampler
                //call callback:  callback("openlid");
                //
                var tokens = what.Split('.');
                if (tokens.Length > 1)
                {
                    string facility = what.Substring(0, what.LastIndexOf('.'));
                    Action<string, MsgHandlerEventArgs> callback;
                    if (_facilityActions.TryGetValue(facility, out callback))
                    {
                        callback(what.Substring(what.LastIndexOf('.') + 1), a);
                    }
                }

            });

            //start participant advertisement
            Task.Factory.StartNew(()=> 
            {
                while(true)
                {
                    //publish("heartbeat", id);
                    advertisement();
                    Thread.Sleep(3000);
                }
            });
        }
    }

    #region instrument participant enums
    //may move these to another project
    /// <summary>
    /// define run states used by all instruments
    /// </summary>
    public enum Runstate
    {
        Idle = 0,
        Pretest,
        Test,
        Posttest
    }

    /// <summary>
    /// define some constants used by instrument participants
    /// </summary>
    public class InstrumentConstants
    {
        public const string RunstateChanged = "RUNSTATE_CHANGED";
    }
    #endregion

    /// <summary>
    /// define an interface that all instrument participants will implement
    /// </summary>
    public interface IInstrumentParticipant
    {

    }

    /// <summary>
    /// example of how to create an instrument participant
    /// inherit from participant and override get, set, and action
    /// doesn't do much but create expectation
    /// concrete instrument participant
    /// meant to be inherited from to create new instrument particpants
    /// </summary>
    public class InstrumentParticipant : Participant, IInstrumentParticipant
    {
        public string location { get; set; }

        public InstrumentParticipant(string address, ECOMMS_Entity.Type type) :
            base(address, Role.Instrument, type)
        {
            location = "NOT SET";
        }
    }

    /// <summary>
    /// define an interface that all service participants will implement
    /// </summary>
    public interface IServiceParticipant
    {

    }

    /// <summary>
    /// concrete service participant
    /// meant to be inherited from to create new service participants
    /// </summary>
    public class ServiceParticipant : Participant
    {
        public ServiceParticipant(
            string id, 
            ECOMMS_Entity.Type type) : base(id, Role.Service, type)
        {
        }
    }
}
