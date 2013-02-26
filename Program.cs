using System;
using System.Net.Sockets;
using System.Net;
using System.Security.Cryptography;
using System.Threading;

namespace ConsoleApplication1
{
    class Program
    {
        //mutex to ensure single instance only of program running
        static Mutex mutex = new Mutex(true, "{8F6F0AC4-B9A1-45fd-A8CF-72F04E6BDE8F}");

        static Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
        static private string guid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        static byte[] buffer = new byte[1024];
        
        [STAThread]
        static void Main(string[] args)
        {
            if (mutex.WaitOne(TimeSpan.Zero, true))
            {
                serverSocket.Bind(new IPEndPoint(IPAddress.Any, 8080));
                serverSocket.Listen(128);
                serverSocket.BeginAccept(null, 0, OnAccept, null);

                Console.WriteLine("Server running");
                Console.WriteLine("Waiting for connection...\n\n");
                Console.Read();
            }
        }

        private static void OnAccept(IAsyncResult result)
        {
            
            try
            {
                Socket client = null;
                
                if (serverSocket != null && serverSocket.IsBound)
                {
                    client = serverSocket.EndAccept(result);
                    handShake(client);//carry out handshake
                    ConnectionSocket clientConnection = new ConnectionSocket(client);
                }
            }
            catch (SocketException exception)
            {
                throw exception;
            }
            finally
            {
                if (serverSocket != null && serverSocket.IsBound)
                {
                    serverSocket.BeginAccept(null, 0, OnAccept, null);
                }
            }
        }//end OnAccept

        private static string AcceptKey(ref string key)
        {
            string longKey = key + guid;
            byte[] hashBytes = ComputeHash(longKey);
            return Convert.ToBase64String(hashBytes);
        }

        static SHA1 sha1 = SHA1CryptoServiceProvider.Create();
        private static byte[] ComputeHash(string str)
        {
            return sha1.ComputeHash(System.Text.Encoding.ASCII.GetBytes(str));
        }

        //method to deal with handshaking client
        private static void handShake(Socket conn)
        {
            string headerResponse = "";
            var i = conn.Receive(buffer);
            headerResponse = (System.Text.Encoding.UTF8.GetString(buffer)).Substring(0, i);
            // write received data to the console
            Console.WriteLine("Client Connected");
            Console.WriteLine(headerResponse);
            if (conn != null)
            {
                /* Handshaking and managing ClientSocket */

                var key = headerResponse.Replace("ey:", "`")
                          .Split('`')[1]                     // dGhlIHNhbXBsZSBub25jZQ== \r\n .......
                          .Replace("\r", "").Split('\n')[0]  // dGhlIHNhbXBsZSBub25jZQ==
                          .Trim();

                // key should now equal dGhlIHNhbXBsZSBub25jZQ==
                var test1 = AcceptKey(ref key);

                var newLine = "\r\n";

                //build handshake response
                var response = "HTTP/1.1 101 Switching Protocols" + newLine
                     + "Upgrade: websocket" + newLine
                     + "Connection: Upgrade" + newLine
                     + "Sec-WebSocket-Accept: " + test1 + newLine + newLine
                     ;

                // send reponse to connected client
                Console.WriteLine("Sending response to client...");
                Console.WriteLine(response);
                conn.Send(System.Text.Encoding.UTF8.GetBytes(response));
                
            }//close if

        }//end handshake

    }//end class

}//end namespace