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
    private byte[] dataBuffer;
    private StringBuilder dataString;
    private Toolkit myToolkit = null;
    private ISession mySession = null;
    private ITerminal[] myTerminals;
    private IAddress[] myAddresses;
    private IContact contact;
    private IConnection[] conn = null;
    private ITerminalConnection terminalConn = null;
    private AccessPermissions perm;
    
  
    //Constructor
    public ConnectionSocket(Socket socket)
    {
        ConnectedSocket = socket;
        dataBuffer = new byte[1024];
        dataString = new StringBuilder();
        //GUID = System.Guid.NewGuid();
        if(connectCCT())
        {
          myTerminals = getTerminals();
          myAddresses = getAddresses();
          sendTerminals();
          sendAddresses();
          createEventHandlers();

        }
        Listen();
           
    }

    //listens for messages from client
    private void Listen()
    {
        ConnectedSocket.BeginReceive(dataBuffer, 0, dataBuffer.Length, 0, OnReceive, null);
    }
    
    //method to send message to client
    private void send(String message)
    {
      byte[] msg = Encoding.UTF8.GetBytes(message);
      int size = msg.Length + 2;//need two bytes for message type and length
      byte[] sending = new byte[size];
      sending[0] = (byte)129;//message type(string)
      sending[1] = (byte)msg.Length;//message length
      for (int i = 0; i < size-2; i++)
        sending[i + 2] = msg[i];
      int byteCount = ConnectedSocket.Send(sending);
    }
    
    //connect to CCT
    private bool connectCCT()
    {
        myToolkit = new Toolkit();
        myToolkit.SessionConnected += new SessionConnectedEventHandler(OnSessionConnectedEvent);
        myToolkit.SessionDisconnected += new SessionDisconnectedEventHandler(OnSessionDisconnectedEvent);
        
        myToolkit.Server = "localhost";
        myToolkit.Credentials = new CCTCredentials();
        try
        {
            mySession = myToolkit.Connect();
            
        }
        catch (OperationFailureException ofe)
        {
            Console.WriteLine("Failed to connect to server. Error: " + ofe.Error.ToString() );
            Thread.Sleep(10000);
            Console.WriteLine("Retrying...");
            connectCCT();
        }
        if (myToolkit.IsConnected)
          return true;
        else
          return false;
    }

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
    
    //Event handlers for connection/disconnection to cct server
    private void OnSessionConnectedEvent(SessionConnectedEventArgs e)
    {
      Console.WriteLine("Connected to CCT server");
      //createEventHandlers();
    }

    private void OnSessionDisconnectedEvent(SessionDisconnectedEventArgs e)
    {
      Console.WriteLine("Lost connection to CCT server");
      while (!myToolkit.IsConnected)
      {
        Console.WriteLine("Attempting to restore connection...");
        connectCCT();
        Thread.Sleep(10000);
      }
    }
    
    //get assigned terminals
    private ITerminal[] getTerminals()
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
      for (int i = 0; i < 5; i++)
      {
        string sAddress = myAddresses[i].ToString();
        send(sAddress);
      }
    }

    private void onTermConnStateChanged(TermConnStateEventArgs e)
    {
        //Console.WriteLine("TerminalConnection to terminal {0} is now in the {1} state.", e.Sender.Name, e.NewState);
        send("local" + e.NewState.ToString());
    }
  
    private void onConnectionPropertyChanged(ConnectionPropertyEventArgs e)
    {
        Console.WriteLine("The {0} property has changed on connection to address {1}.", e.ChangedProperty, e.Address.Name);
    }

    private void onConnectionStateChange(ConnectionStateEventArgs args)
    {
        Console.WriteLine("Remote connection to address {0} is now in the {1} state.", args.Address.Name, args.NewState);
        send("remote" + args.NewState.ToString());
    }

    private void onContactEnteringScope(ContactScopeEventArgs args)
    {
        Console.WriteLine("Contact {0} has entered scope.", args.Contact.ID);
        contact = args.Contact;
        conn = contact.Connections;
        
        getContactProperties();
    }

    private void onContactLeavingScope(ContactScopeEventArgs args)
    {
        Console.WriteLine("Contact {0} has left scope.", args.Contact.ID);
        contact = null;
    }

    

    //invoked when message received from client
    private void OnReceive(IAsyncResult result)
    {
        int sizeOfReceivedData = ConnectedSocket.EndReceive(result);
        if (sizeOfReceivedData > 0)
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
            for(int i = maskIndex, j = 0; i < (maskIndex + 4); i++)
            {
                masks[j] = dataBuffer[i];
                j++;
            }

            //get message bytes and decode using the masks
            int messageLength = sizeOfReceivedData - start;
            byte[] message = new byte[messageLength];

            for(int i = start, j = 0; i < sizeOfReceivedData; i++, j++)
            {
                message[j] = (byte) (dataBuffer[i] ^ masks[j % 4]);
            }
            
            //convert message bytes to characters and append to dataString
            dataString.Append(Encoding.UTF8.GetString(message));
            string msg = dataString.ToString();
            //Console.WriteLine(msg);

            dataString = null;
            dataString = new StringBuilder();
            
            //Call method based on content of msg
            if (msg == "answer")
                answerCall();
            else if (msg == "hold")
                holdCall();
            else if (msg == "unhold")
                unholdCall();
            else if (msg == "transfer")
                transferCall();
            else if (msg == "release")
                releaseCall();
            else if (msg == "mute")
                muteCall();
            else if (msg == "unmute")
                unMuteCall();
            else if (msg == "conf")
                conferenceCall();
            else if (msg.Contains("originate"))
                originateCall(msg);
            else if (msg == "")
                closeConnection();
     }
        
     //Listen for more messages
     if(ConnectedSocket.Connected)
        Listen();
        
    }//end on receive

    //method to originate calls
    private void originateCall(string message)
    { 
        string number;
        int addr, term;
        
        string[] s = message.Split(' ');
        number = s[1];
        addr = int.Parse(s[2]) - 1;
        term = int.Parse(s[3]) - 1;
         
        Console.WriteLine("Originate call method");
        Console.WriteLine("Dest: " + number + " Terminal: {0} Address: {1}", term, addr);
        if (myAddresses[addr].Capabilities.CanOriginate)
        {
            
            try
            {
                myTerminals[term].Originate(myAddresses[addr], number);
            }
            catch (OperationFailureException ofe)
            {
                Console.WriteLine("Originate call failed: " + ofe.Error.ToString());
            }
        }
        else
          Console.WriteLine("Address cannot originate");
        
    }

    private void answerCall()
    {
        if(conn[0].CurrentState.ToString()== "Alerting")
            terminalConn = conn[0].TerminalConnections[0];
        else if (conn[1].CurrentState.ToString() == "Alerting")
            terminalConn = conn[1].TerminalConnections[0];
        TerminalConnectionState state = terminalConn.CurrentState;
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
            Console.WriteLine("Cannot answer call");
     }
    

    private void holdCall()
    {
        
        if (conn != null)
        {
            if (perm.ToString() != "Identify")
                terminalConn = conn[0].TerminalConnections[0];
            else
                terminalConn = conn[1].TerminalConnections[0];
            TerminalConnectionState state = terminalConn.CurrentState;
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
                Console.WriteLine("Cannot Hold Call");
        }
    }

    private void unholdCall()
    {
        AccessPermissions perm = conn[0].Permissions;
        if (conn != null)
        {
            if (perm.ToString() != "Identify")
                terminalConn = conn[0].TerminalConnections[0];
            else
                terminalConn = conn[1].TerminalConnections[0];
            if (terminalConn.Capabilities.CanUnhold)
            {
                terminalConn.Unhold();
                Console.WriteLine("Call Unheld");
            }
            else
                Console.WriteLine("Cannot Unhold call");

        }
    }

    private void transferCall()
    {
        Console.WriteLine("Transfer call method");
    }
    private void releaseCall()
    {
        Console.WriteLine("Release call method");
        if (conn != null)
        {
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
                Console.WriteLine("Cannot Disconnect");
        }
    }

    private void muteCall()
    {
        Console.WriteLine("Mute call method");
        if (conn != null)
        {
            terminalConn = conn[0].TerminalConnections[0];
            if (terminalConn.Capabilities.CanMute && !terminalConn.IsMuted)
            {
                terminalConn.Mute();
                Console.WriteLine("Call Muted");
            }
            else
                Console.WriteLine("Cannot Mute Call");
        }
    }

    private void unMuteCall()
    {
        Console.WriteLine("UnMute call method");
        if (conn != null)
        {
            terminalConn = conn[0].TerminalConnections[0];
            if (terminalConn.IsMuted)
            {
                terminalConn.Hold();
                Console.WriteLine("Call Unmuted");
            }
            else
                Console.WriteLine("Cannot Unmute Call");
        }
    }

    private void conferenceCall()
    {
        Console.WriteLine("Conference call method");
    }

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
        send(properties);
    }

    //Close socket connection
    private void closeConnection()
    {
        ConnectedSocket.Shutdown(SocketShutdown.Both);
        ConnectedSocket.Close();
        myToolkit.Disconnect();
        Console.WriteLine("Client Disconnected\n");
    }
}//end class

