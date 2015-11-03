﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Windows.Threading;
using Lidgren.Network;
using Pulsar4X.ECSLib;
using Pulsar4X.ViewModel;

namespace Pulsar4x.Networking
{
    public class NetworkBase
    {
        protected Game _game_ { get { return _gameVM_.Game; }}
        protected GameVM _gameVM_ { get; set; }

        private readonly ObservableCollection<string> _messages;
        public ObservableCollection<string> Messages { get {return _messages;} }

        public int PortNum { get; set; }
        public NetPeer NetPeerObject { get; set; }

        public NetworkBase()
        {
            _messages = new ObservableCollection<string>();
        }

        protected void StartListning()
        {            
            NetPeerObject.RegisterReceivedCallback(new SendOrPostCallback(GotMessage)); 
        }

        public void GotMessage(object peer)
        {
            var message = NetPeerObject.ReadMessage();

            
            switch (message.MessageType)
                {
                    case NetIncomingMessageType.Data:
                        // handle custom messages
                        Messages.Add("Data Message from: " + message.SenderConnection.RemoteUniqueIdentifier);
                        HandleIncomingMessage(message.SenderConnection, message.Data);
                        break;

                    case NetIncomingMessageType.DiscoveryRequest:
                        HandleDiscoveryRequest(message);
                        break;
                    case NetIncomingMessageType.DiscoveryResponse:
                        HandleDiscoveryResponce(message);
                        break;

                    case NetIncomingMessageType.StatusChanged:
                        // handle connection status messages
                        Messages.Add("New status: " + message.SenderConnection.Status + " (Reason: " + message.ReadString() + ")");
                        ConnectionStatusChanged(message);
                        break;

                    case NetIncomingMessageType.DebugMessage:
                        // handle debug messages
                        // (only received when compiled in DEBUG mode)
                        Messages.Add("Debug Msg: " + message.ReadString());
                        break;

                    /* .. */
                    default:
                         Messages.Add(("unhandled message with type: "
                            + message.MessageType));
                        break;
                }
        } 

        //protected void MessageListner()
        //{
        //    NetIncomingMessage message;
        //    while ((message = NetPeerObject.ReadMessage()) != null)
        //    {
        //        switch (message.MessageType)
        //        {
        //            case NetIncomingMessageType.Data:
        //                // handle custom messages
        //                HandleIncomingMessage(message.SenderConnection, message.Data);
        //                break;

        //            case NetIncomingMessageType.StatusChanged:
        //                // handle connection status messages
        //                switch (message.SenderConnection.Status)
        //                {
        //                    /* .. */
        //                }
        //                break;

        //            case NetIncomingMessageType.DebugMessage:
        //                // handle debug messages
        //                // (only received when compiled in DEBUG mode)
        //                Console.WriteLine(message.ReadString());
        //                break;

        //            /* .. */
        //            default:
        //                Console.WriteLine("unhandled message with type: "
        //                    + message.MessageType);
        //                break;
        //        }
        //    }
        //}


        protected virtual void HandleDiscoveryRequest(NetIncomingMessage message)
        {
        }
        protected virtual void HandleDiscoveryResponce(NetIncomingMessage message)
        {
        }

        protected virtual void HandleIncomingMessage(NetConnection sender, byte[] data)
        {
        }

        protected virtual void ConnectionStatusChanged(NetIncomingMessage message)
        {
        }

        protected void HandleIncomingStringMessage(NetConnection sender, DataMessage dataMessage)
        {
            Messages.Add(sender.RemoteUniqueIdentifier + " Sent: " + (string)dataMessage.DataObject);
        }

    }

    public class NetworkHost : NetworkBase
    {
        private Dictionary<NetConnection, Guid> _connectedFactions { get; set; }
        private Dictionary<Guid, List<NetConnection>> _factionConnections { get; set; } 
               
        public NetServer NetServerObject { get { return (NetServer)NetPeerObject; }}

        public NetworkHost(GameVM gameVM, int portNum)
        {
            _gameVM_ = gameVM;
            PortNum = portNum;
        }

        public void ServerStart()
        {
            var config = new NetPeerConfiguration("Pulsar4X") { Port = PortNum };
            config.EnableMessageType(NetIncomingMessageType.DiscoveryRequest);
            NetPeerObject = new NetServer(config);
            NetPeerObject.Start();
            _connectedFactions = new Dictionary<NetConnection, Guid>();
            _factionConnections = new Dictionary<Guid, List<NetConnection>>();
            StartListning();
        }


        protected override void HandleDiscoveryRequest(NetIncomingMessage message)
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Messages.Add("RX DiscoveryRequest " + message.SenderEndPoint)));
            
            Messages.Add("RX DiscoveryRequest " + message.SenderEndPoint);
            NetOutgoingMessage response = NetServerObject.CreateMessage();
            response.Write("Pulsar4x Server Game: " + _game_.GameName);
            NetServerObject.SendDiscoveryResponse(response, message.SenderEndPoint);
        }

        protected override void HandleIncomingMessage(NetConnection fromConnection, byte[] data)
        {
            DataMessage dataMessage;
            BinaryFormatter formatter = new BinaryFormatter();

            var mStream = new MemoryStream(data);            
            dataMessage = (DataMessage)formatter.Deserialize(mStream);

            if (dataMessage.DataMessageType == DataMessageType.FactionDictionary) //we're server so this is a request.
            {
                SendFactionList(fromConnection);
            }

        }

        protected override void ConnectionStatusChanged(NetIncomingMessage message)
        {
            switch (message.SenderConnection.Status)
            {
                case NetConnectionStatus.Connected:                    
                    break;
                case NetConnectionStatus.Disconnected:                    
                    break;
            }


        }

        private void SetSendMessages()
        {
            
            //set events in the processors? or move this into the ecslib...
        }

        private void SendFactionList(NetConnection recipient)
        {
            //list of factions: 
            
            List<Entity> factions = _game_.GlobalManager.GetAllEntitiesWithDataBlob<FactionInfoDB>();
            //we don't want to send the whole entitys, just a dictionary of guid ID and the string name. 
            Dictionary<Guid,string> factionGuidNames = factions.ToDictionary(faction => faction.Guid, faction => faction.GetDataBlob<NameDB>().DefaultName);

            DataMessage dataMessage = new DataMessage {DataMessageType = DataMessageType.FactionDictionary, DataObject = factionGuidNames};

            //turn the dictionary into a stream.
            var binFormatter = new BinaryFormatter();
            var mStream = new MemoryStream();
            binFormatter.Serialize(mStream, dataMessage);

            //This gives you the byte array.
            //mStream.ToArray();

            NetOutgoingMessage sendMsg = NetServerObject.CreateMessage();
            sendMsg.Write(mStream.ToArray()); //send the stream as an byte array. 
            sendMsg.Write(42);
            
            NetServerObject.SendMessage(sendMsg, recipient, NetDeliveryMethod.ReliableOrdered);
        }


    }
    public enum DataMessageType
    {
        StringMessage,
        FactionDictionary,
        DataBlobPropertyUpdate,
       

    }
    [Serializable]
    public class DataMessage
    {
        public DataMessageType DataMessageType { get; set; }
        public object DataObject { get; set; }

        public Guid EntityGuid { get; set; }
        public string PropertyName { get; set; }//actualy can I look at how wpf does this?
    }


    public class NetworkClient : NetworkBase
    {

        private NetClient NetClientObject { get { return (NetClient)NetPeerObject; } }
        public string HostAddress { get; set; }
        private bool _isConnectedToServer;
        public bool IsConnectedToServer { get { return _isConnectedToServer; } set { _isConnectedToServer = value; OnPropertyChanged(); } }

        public NetworkClient(GameVM gameVM, string hostAddress, int portNum)
        {
            _gameVM_ = gameVM;
            PortNum = portNum;
            HostAddress = hostAddress;
            IsConnectedToServer = false;
        }

        public void ClientConnect()
        {
            var config = new NetPeerConfiguration("Pulsar4X");
            config.EnableMessageType(NetIncomingMessageType.DiscoveryResponse);
            NetPeerObject = new NetClient(config);
            NetPeerObject.Start();
            NetClientObject.DiscoverLocalPeers(PortNum);
            //NetPeerObject.Connect(host: HostAddress, port: PortNum);
            //StartListning();
            
        }


        protected override void HandleDiscoveryResponce(NetIncomingMessage message)
        {
            Messages.Add("Found Server: " + message.SenderEndPoint + "Name Is: " + message.ReadString());
        }

        protected override void HandleIncomingMessage(NetConnection fromConnection, byte[] data)
        {
            DataMessage dataMessage;
            BinaryFormatter formatter = new BinaryFormatter();

            var mStream = new MemoryStream(data);
            dataMessage = (DataMessage)formatter.Deserialize(mStream);

            switch (dataMessage.DataMessageType)
            {
                case DataMessageType.StringMessage:
                     HandleIncomingStringMessage(fromConnection, dataMessage);
                    break;
                case DataMessageType.FactionDictionary:
                    ReceveFactionList(dataMessage);
                    break;


            }
        }

        protected override void ConnectionStatusChanged(NetIncomingMessage message)
        {
            switch (message.SenderConnection.Status)
            {
                case  NetConnectionStatus.Connected:               
                    IsConnectedToServer = true;               
                    break;
                case NetConnectionStatus.Disconnected:
                    IsConnectedToServer = false;
                    break;
            }
             
 
        }

        public void ReceveFactionList(DataMessage dataMessage)
        {
            Dictionary<Guid, string> factionList =  (Dictionary<Guid,string>)dataMessage.DataObject;
        }

        public void SendFactionListRequest()
        {            
            DataMessage dataMessage = new DataMessage();
            dataMessage.DataMessageType = DataMessageType.FactionDictionary;

            var binFormatter = new BinaryFormatter();
            var mStream = new MemoryStream();
            binFormatter.Serialize(mStream, dataMessage);


            NetOutgoingMessage sendMsg = NetClientObject.CreateMessage();
            sendMsg.Write(mStream.ToArray()); //send the stream as an byte array. 
            sendMsg.Write(42);

            NetClientObject.SendMessage(sendMsg, NetClientObject.ServerConnection, NetDeliveryMethod.ReliableOrdered);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        [NotifyPropertyChangedInvocator]
        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

}
