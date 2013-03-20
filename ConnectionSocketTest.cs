using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using System.Net.Sockets;
using System.Net;
using Nortel.CCT;

[TestFixture]
class ConnectionSocketTest
{
    
    ConnectionSocket test;

    [SetUp]
    public void setUp()
    {
        test = new ConnectionSocket();
    }   

    [TearDown]
    public void tearDown()
    {
        test = null;
    }

    [Test]
    public void testEncode()
    {
        byte[] expected = new byte[6];
        expected[0] = 129;//type string
        expected[1] = 4;//length of data
        expected[2] = 116;//t
        expected[3] = 101;//e
        expected[4] = 115;//s
        expected[5] = 116;//t
        byte[] result = test.encode("test");
        Assert.AreEqual(expected, result);
    }

    [Test]
    public void testDecode()
    {
        string expected = "test";
        byte[] msg = new byte[10];
        msg[0] = 129;
        msg[1] = 132;
        msg[2] = 95;
        msg[3] = 139;
        msg[4] = 207;
        msg[5] = 21;
        msg[6] = 43;
        msg[7] = 238;
        msg[8] = 188;
        msg[9] = 97;
        string result = test.decode(msg, 10);
        Assert.AreEqual(expected, result);
    }
}

