using System;
using System.Net.Sockets;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Text;
using Nortel.CCT;

class ConnectionSocket
{
    public Socket ConnectedSocket { get; private set; }
    private byte[] dataBuffer;//buffer for receiving data
    private StringBuilder dataString;//string builder for building strings that will be sent to client
    //CCT variables
    private Toolkit myToolkit = null;
    private ISession mySession = null;
    private ITerminal[] myTerminals;
    public IAddress[] myAddresses;
    public IContact contact;
    private IConnection[] conn = null;
    private ITerminalConnection terminalConn = null;
    private AccessPermissions perm;
    private IContact transfer = null;
    private IContact conference = null;
    private ITerminalConnection transferTerminalConn;
    private ITerminalConnection conferenceTerminalConn;
    
  
    //Constructor
    public ConnectionSocket(Socket socket)
    {
        ConnectedSocket = socket;
        dataBuffer = new byte[1024];
        dataString = new StringBuilder();
        if(connectCCT())
        {
            myTerminals = getTerminals();
            myAddresses = getAddresses();
            sendTerminals();
            sendAddresses();
            createEventHandlers();
        }
        Listen();//begin listening for mesaages from connected client
           
    }

    //Constructor for unit testing that does not connect to CCT
    public ConnectionSocket()
    {
        dataBuffer = new byte[1024];
        dataString = new StringBuilder();
    }

    //listens for messages from client
    private void Listen()
    {
        ConnectedSocket.BeginReceive(dataBuffer, 0, dataBuffer.Length, 0, OnReceive, null);
    }
    
    //method to send message to client
    public void send(String message)
    {
        byte[] msg = encode(message);
        //send encoded message
        ConnectedSocket.Send(msg);
    }

    //messages for encoding messages that will be sent to clients
    public byte[] encode(String message)
    {
        byte[] msg = Encoding.UTF8.GetBytes(message);
        int size = msg.Length + 2;//need two bytes for message type and length
        byte[] sending = new byte[size];
        sending[0] = (byte)129;//message type(string)
        sending[1] = (byte)msg.Length;//message length
        for (int i = 0; i < size - 2; i++)
            sending[i + 2] = msg[i];
        return sending;
    }
    
    //connect to CCT
    private bool connectCCT()
    {
        myToolkit = new Toolkit();//create toolkit object
        //register event handlers on Toolkit object for connecting/disconnecting from CCT
        myToolkit.SessionConnected += new SessionConnectedEventHandler(OnSessionConnectedEvent);
        myToolkit.SessionDisconnected += new SessionDisconnectedEventHandler(OnSessionDisconnectedEvent);
        
        myToolkit.Server = "localhost";
        myToolkit.Credentials = new CCTCredentials();
        try
        {
            mySession = myToolkit.Connect();//connect to CCT server
            
        }
        catch (OperationFailureException ofe)
        {
            Console.WriteLine("Failed to connect to server. Error: " + ofe.Error.ToString() );
            //if connection to CCT failed wait 10 sec and retry
            Thread.Sleep(10000);
            Console.WriteLine("Retrying...");
            connectCCT();
        }
        //return whether connection is established or not
        if (myToolkit.IsConnected)
          return true;
        else
          return false;
    }

    //register event handlers for CCT events
    private void createEventHandlers()
    {
        ConnectionPropertyEventHandler handler = myToolkit.CreateEventHandler(new ConnectionPropertyEventHandler(onConnectionPropertyChanged));
        mySession.ConnectionPropertyChanged += handler;
        TermConnStateEventHandler hand = myToolkit.CreateEventHandler(new TermConnStateEventHandler(onTermConnStateChanged));
        mySession.TermConnStateChanged += hand;
        ConnectionStateEventHandler connStateEventHandler = myToolkit.CreateEventHandler(new ConnectionStateEventHandler(onConnectionStateChange));
        mySession.RemoteConnectionStateChanged += connStateEventHandler;
        ContactScopeEventHandler contactEnteringScopeHandler = myToolkit.CreateEventHandler(new ContactScopeEventHandler(onContactEnteringScope));
        mySession.ContactEnteringScope += contactEnteringScopeHandler;
        ContactScopeEventHandler contactLeavingScopeHandler = myToolkit.CreateEventHandler(new ContactScopeEventHandler(onContactLeavingScope));
        mySession.ContactLeavingScope += contactLeavingScopeHandler;
    }
       
    
    //get assigned terminals
    public ITerminal[] getTerminals()
    {
      ITerminal[] myTerminals = mySession.Terminals;
      return myTerminals;
    }
    
    //get assigned addresses
    private IAddress[] getAddresses()
    {
      IAddress[] myAddresses = mySession.Addresses;
      return myAddresses;
    }

    //send terminals to client
    private void sendTerminals()
    {
      foreach (ITerminal terminal in myTerminals)
      {
        string sTerminal = terminal.ToString();
        send(sTerminal);
      }
    }

    //send addresses to client
    private void sendAddresses()
    {
      for (int i = 0; i < 4; i++)
      {
        string sAddress = myAddresses[i].ToString();
        send(sAddress);
      }
    }

    #region CCT Event Handlers
    //Event handlers for connection to cct server
    private void OnSessionConnectedEvent(SessionConnectedEventArgs e)
    {
        Console.WriteLine("Connected to CCT server");
    }

    //Event handlers for disconnection from cct server
    private void OnSessionDisconnectedEvent(SessionDisconnectedEventArgs e)
    {
        Console.WriteLine("Lost connection to CCT server");
        send("Error: Lost connection to CCT server");
        //if connection is lost attempt to restore
        while (!myToolkit.IsConnected)
        {
            Console.WriteLine("Attempting to restore connection...");
            connectCCT();
            Thread.Sleep(10000);
        }
    }
    //event handler for Terminal Connection State Change
    //represents a change to the local state of call
    private void onTermConnStateChanged(TermConnStateEventArgs e)
    {
        //send new state to client
        send("local" + e.NewState.ToString());
    }
  
    private void onConnectionPropertyChanged(ConnectionPropertyEventArgs e)
    {
        Console.WriteLine("The {0} property has changed on connection to address {1}.", e.ChangedProperty, e.Address.Name);
    }

    //event handler for Connection State Changed
    //represents a change of the remote state of the call
    private void onConnectionStateChange(ConnectionStateEventArgs args)
    {
        //send new remote state to client
        send("remote" + args.NewState.ToString());
    }

    //event handler for Contact Entering Scope
    //represents a new incoming or outgoing call
    private void onContactEnteringScope(ContactScopeEventArgs args)
    {
        Console.WriteLine("Contact {0} has entered scope.", args.Contact.ID);
        contact = args.Contact;//store contact 
        conn = contact.Connections;//get connections from contact
        //get the properties of new connection
        getContactProperties();
    }

    //event handler for Contact Leaving Scope
    //represents a call ending
    private void onContactLeavingScope(ContactScopeEventArgs args)
    {
        Console.WriteLine("Contact {0} has left scope.", args.Contact.ID);
        contact = null;
    }
    #endregion

    //invoked when message received from client
    private void OnReceive(IAsyncResult result)
    {
        int sizeOfReceivedData = ConnectedSocket.EndReceive(result);
        if (sizeOfReceivedData > 0)
        {
            //decode received message
            string msg = decode(dataBuffer, sizeOfReceivedData);
            
            //Call method based on content of msg
            if (msg == "answer")
                answerCall();
            else if (msg == "hold")
                holdCall();
            else if (msg == "unhold")
                unholdCall();
            else if (msg.Contains("transfer"))
                transfer = transferCall(msg);
            else if (msg == "complete")
                completeTransfer(transfer);
            else if (msg == "release")
                releaseCall();
            else if (msg == "mute")
                muteCall();
            else if (msg == "unmute")
                unMuteCall();
            else if (msg.Contains("conf"))
                conference = conferenceCall(msg);
            else if (msg == "completeConf")
                completeConference(conference);
            else if (msg.Contains("originate"))
                originateCall(msg);
            else if (msg == "")
                closeConnection();
     }
        
     //Listen for more messages
     if(ConnectedSocket.Connected)
        Listen();
        
    }//end on receive

    //decodes messages received from clients
    public string decode(byte[] buffer, int size)
    {
        /*dataBuffer is a byte array containing the received message
             * sizeOfReceivedData gives the number of bytes received
             * The first byte is the type of data - since only string messages are being sent it will always be 129
             * The second byte gives length - will always be one byte in this case as messages are short
             * The next 4 bytes are the masks for decoding the message
             * The remaining bytes are the actual message*/

        int start = 6;//start position of data

        //get masks bytes
        int maskIndex = 2;
        byte[] masks = new byte[4];
        for (int i = maskIndex, j = 0; i < (maskIndex + 4); i++)
        {
            masks[j] = buffer[i];
            j++;
        }

        //get message bytes and decode using the masks
        int messageLength = size - start;
        byte[] message = new byte[messageLength];

        for (int i = start, j = 0; i < size; i++, j++)
        {
            message[j] = (byte)(buffer[i] ^ masks[j % 4]);
        }

        //convert message bytes to characters and append to dataString
        dataString.Append(Encoding.UTF8.GetString(message));
        string msg = dataString.ToString();

        dataString = null;
        dataString = new StringBuilder();
        return msg;
    }

    #region CallControlMethods
    //method to originate calls
    public void originateCall(string message)
    { 
        string number;
        int addr, term;
        //get destination address and originating terminal/address from message
        string[] s = message.Split(' ');
        number = s[1];
        addr = int.Parse(s[2]) - 1;
        term = int.Parse(s[3]) - 1;
         
        Console.WriteLine("Dest: " + number + " Terminal: {0} Address: {1}", term, addr);
        //check that originating address can originate
        if (myAddresses[addr].Capabilities.CanOriginate)
        {

            try
            {
                myTerminals[term].Originate(myAddresses[addr], number);
            }
            catch (OperationFailureException ofe)
            {
                Console.WriteLine("Originate call failed: " + ofe.Error.ToString());
                send("Error: Originate call failed");
            }
        }
        else
        {
            Console.WriteLine("Address cannot originate");
            send("Error: Address cannot originate");
        }
        
    }

    //method to answer incoming calls
    private void answerCall()
    {
        if(conn[0].CurrentState.ToString()== "Alerting")
            terminalConn = conn[0].TerminalConnections[0];
        else if (conn[1].CurrentState.ToString() == "Alerting")
            terminalConn = conn[1].TerminalConnections[0];
        TerminalConnectionState state = terminalConn.CurrentState;
        //check that the termianl connection can answer calls and that the local state is ringing
        if (terminalConn.Capabilities.CanAnswer && state == TerminalConnectionState.Ringing)
        {
            try
            {
                terminalConn.Answer();
                Console.WriteLine("Call Answered");
            }
            catch (OperationFailureException e)
            {
                Console.WriteLine(e.Error.ToString());
            }
        }
        else
        {
            Console.WriteLine("Cannot answer call");
            send("Error: Cannot answer call");
        }
     }
    
    //method to hold active call
    private void holdCall()
    {
        
        if (conn != null)
        {
            if (perm.ToString() != "Identify")
                terminalConn = conn[0].TerminalConnections[0];
            else
                terminalConn = conn[1].TerminalConnections[0];
            TerminalConnectionState state = terminalConn.CurrentState;
            //check that the terminal connection can hold and that the local state of the call is active
            if (terminalConn.Capabilities.CanHold && state == TerminalConnectionState.Active)
            {
                try
                {
                    terminalConn.Hold();
                    Console.WriteLine("Call Held");
                }
                catch (OperationFailureException e)
                {
                    Console.WriteLine(e.Error.ToString());
                }
            }
            else
            {
                Console.WriteLine("Cannot Hold Call");
                send("Error: Cannot Hold Call");
            }

        }
    }

    //method to unhold held call
    private void unholdCall()
    {
        AccessPermissions perm = conn[0].Permissions;
        if (conn != null)
        {
            if (perm.ToString() != "Identify")
                terminalConn = conn[0].TerminalConnections[0];
            else
                terminalConn = conn[1].TerminalConnections[0];
            //check that the terminal connection can unhold
            if (terminalConn.Capabilities.CanUnhold)
            {
                terminalConn.Unhold();
                Console.WriteLine("Call Unheld");
            }
            else
            {
                Console.WriteLine("Cannot Unhold call");
                send("Error: Cannot Unhold call");
            }

        }
    }
    
    //method to begin supervised transfer
    private IContact transferCall(string message)
    {
        //get number to transfer to from message
        string number;
        string[] s = message.Split(' ');
        number = s[1];
        IContact trans = null;
        
        AccessPermissions perm = conn[0].Permissions;
        if (conn != null)
        {
            if (perm.ToString() != "Identify")
                transferTerminalConn = conn[0].TerminalConnections[0];
            else
                transferTerminalConn = conn[1].TerminalConnections[0];
            //check that the transfer can be initiated
            if (transferTerminalConn.Capabilities.CanInitiateTransfer)
            {
                trans = transferTerminalConn.InitiateSupervisedTransfer(number);
                Console.WriteLine("Transfer Initiated");
                
            }
            else
            {
                Console.WriteLine("Cannot initiate transfer call");
                send("Error: Cannot initiate transfer call");
            }

        }
        //return new contact. This is used when completing transfer
        return trans;
    }

    //method to complete supervised transfer of call
    private void completeTransfer(IContact trans)
    {
        //check that the transfer can be completed
        if (transferTerminalConn.Capabilities.CanCompleteTransfer)
        {
            //add try here and catch operation failure exception?
            transferTerminalConn.CompleteSupervisedTransfer(trans);
            Console.WriteLine("Transfer Completed");
            transferTerminalConn = null;
        }
        else
        {
            Console.WriteLine("Cannot complete transfer call");
            send("Error: Cannot complete transfer call");
        }
        
    }
    
    //method to begin conference call
    private IContact conferenceCall(string message)
    {
        //get number to add to conference call
        string number;
        string[] s = message.Split(' ');
        number = s[1];
        IContact conf = null;

        AccessPermissions perm = conn[0].Permissions;
        if (conn != null)
        {
            if (perm.ToString() != "Identify")
                conferenceTerminalConn = conn[0].TerminalConnections[0];
            else
                conferenceTerminalConn = conn[1].TerminalConnections[0];
            //check that terminal connection can initiate conference call
            if (conferenceTerminalConn.Capabilities.CanInitiateConference)
            {
                conf = conferenceTerminalConn.InitiateConference(number);
                Console.WriteLine("Conference Initiated");

            }
            else
            {
                Console.WriteLine("Cannot create conference call");
                send("Error: Cannot create conference call");
            }

        }
        //return reference to new contact. This is used when completing conference call
        return conf;
    }
    
    //method to complete conference call
    private void completeConference(IContact conf)
    {
        //check that conference can be completed
        if (conferenceTerminalConn.Capabilities.CanCompleteConference)
        {
            //add try catch here
            conferenceTerminalConn.CompleteConference(conf);
            Console.WriteLine("Conference Completed");
            conferenceTerminalConn = null;
        }
        else
        {
            Console.WriteLine("Cannot complete conference call");
            send("Error: Cannot complete conference call");
        }

    }

    //method to release active call
    public void releaseCall()
    {
        Console.WriteLine("Release call method");
        if (conn != null)
        {
            //release the connection that can disconnect(local leg of call)
            if (conn[0].Capabilities.CanDisconnect)
            {
                conn[0].Disconnect();
                conn = null;
                contact = null;
                Console.WriteLine("Call disconnected");
            }
            else if (conn[1].Capabilities.CanDisconnect)
            {
                conn[1].Disconnect();
                conn = null;
                contact = null;
                Console.WriteLine("Call disconnected");
            }
            else
            {
                Console.WriteLine("Cannot Disconnect");
                send("Error: Cannot disconnect");
            }
        }
        else
        {
            Console.WriteLine("No Connection");
            send("Error: No Connection");
        }
    }

    //method to mute active call
    private void muteCall()
    {
        AccessPermissions perm = conn[0].Permissions;
        if (conn != null)
        {
            if (perm.ToString() != "Identify")
                terminalConn = conn[0].TerminalConnections[0];
            else
                terminalConn = conn[1].TerminalConnections[0];
            //check that call is not muted and can mute
            //note that muting not supported by CCT simulation used so CanMute is always false
            if (terminalConn.Capabilities.CanMute && !terminalConn.IsMuted)
            {
                terminalConn.Mute();
                Console.WriteLine("Call Muted");
            }
            else
            {
                Console.WriteLine("Cannot Mute Call");
                send("Error: Cannot mute call");
            }
        }
    }

    //method to unmute muted call
    private void unMuteCall()
    {
        AccessPermissions perm = conn[0].Permissions;
        if (conn != null)
        {
            if (perm.ToString() != "Identify")
                terminalConn = conn[0].TerminalConnections[0];
            else
                terminalConn = conn[1].TerminalConnections[0];
            //check that call is muted and can mute
            if (terminalConn.IsMuted && terminalConn.Capabilities.CanMute)
            {
                terminalConn.Unmute();
                Console.WriteLine("Call Unmuted");
            }
            else
            {
                Console.WriteLine("Cannot Unmute Call");
                send("Error: Cannot unmute call");
            }
        }
    }

    #endregion

    //method for getting properties of new contacts
    private void getContactProperties()
    {
        string called = contact.CalledAddress;
        string callingAddress = contact.CallingAddress;
        string[] contactType = contact.ContactTypes;
        string ct = contactType[0];
        Console.WriteLine("Called: " + called);
        Console.WriteLine("Calling Address: " + callingAddress);
        Console.WriteLine("Type: " + ct);
        perm = conn[0].Permissions;
        if (perm.ToString() != "Identify")
            terminalConn = conn[0].TerminalConnections[0];
        else
            terminalConn = conn[1].TerminalConnections[0];
        TerminalConnectionState state = terminalConn.CurrentState;
        string localState = state.ToString();
        string properties = "properties " + called + " " + callingAddress + " " + ct + " " + localState;
        //send properties to client
        send(properties);
    }

    //Close socket connection
    private void closeConnection()
    {
        ConnectedSocket.Shutdown(SocketShutdown.Both);
        ConnectedSocket.Close();
        Console.WriteLine("Client Disconnected\n");
    }
}//end class

