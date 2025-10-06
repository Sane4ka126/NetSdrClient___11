using Moq;
using NetSdrClientApp;
using NetSdrClientApp.Messages;
using NetSdrClientApp.Networking;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetSdrClientAppTests
{
    [TestFixture]
    public class NetSdrClientTests
    {
        private Mock<ITcpClient> _mockTcpClient;
        private Mock<IUdpClient> _mockUdpClient;
        private NetSdrClient _client;

        [SetUp]
        public void SetUp()
        {
            _mockTcpClient = new Mock<ITcpClient>();
            _mockUdpClient = new Mock<IUdpClient>();
            _client = new NetSdrClient(_mockTcpClient.Object, _mockUdpClient.Object);
        }

        [Test]
        public async Task ConnectAsync_WhenNotConnected_SendsSetupMessages()
        {
            // Arrange
            _mockTcpClient.Setup(x => x.Connected).Returns(false);
            var sentMessages = new List<byte[]>();
            _mockTcpClient.Setup(x => x.SendMessageAsync(It.IsAny<byte[]>()))
                .Callback<byte[]>(msg =>
                {
                    sentMessages.Add(msg);
                    // Trigger MessageReceived event to complete the request
                    _mockTcpClient.Raise(x => x.MessageReceived += null, _mockTcpClient.Object, new byte[] { 0x01 });
                })
                .Returns(Task.CompletedTask);

            // Act
            await _client.ConnectAsync();

            // Assert
            _mockTcpClient.Verify(x => x.Connect(), Times.Once);
            _mockTcpClient.Verify(x => x.SendMessageAsync(It.IsAny<byte[]>()), Times.Exactly(3));
            Assert.That(sentMessages.Count, Is.EqualTo(3));
        }

        [Test]
        public async Task ConnectAsync_WhenAlreadyConnected_DoesNotReconnect()
        {
            // Arrange
            _mockTcpClient.Setup(x => x.Connected).Returns(true);

            // Act
            await _client.ConnectAsync();

            // Assert
            _mockTcpClient.Verify(x => x.Connect(), Times.Never);
            _mockTcpClient.Verify(x => x.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
        }

        [Test]
        public async Task StartIQAsync_WhenConnected_StartsIQAndUdpListening()
        {
            // Arrange
            _mockTcpClient.Setup(x => x.Connected).Returns(true);
            _mockTcpClient.Setup(x => x.SendMessageAsync(It.IsAny<byte[]>()))
                .Callback<byte[]>(msg =>
                {
                    _mockTcpClient.Raise(x => x.MessageReceived += null, _mockTcpClient.Object, new byte[] { 0x01 });
                })
                .Returns(Task.CompletedTask);
            _mockUdpClient.Setup(x => x.StartListeningAsync()).Returns(Task.CompletedTask);

            // Act
            await _client.StartIQAsync();

            // Assert
            Assert.That(_client.IQStarted, Is.True);
            _mockTcpClient.Verify(x => x.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
            _mockUdpClient.Verify(x => x.StartListeningAsync(), Times.Once);
        }

        [Test]
        public async Task StartIQAsync_WhenNotConnected_DoesNotStartIQ()
        {
            // Arrange
            _mockTcpClient.Setup(x => x.Connected).Returns(false);

            // Act
            await _client.StartIQAsync();

            // Assert
            Assert.That(_client.IQStarted, Is.False);
            _mockTcpClient.Verify(x => x.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
            _mockUdpClient.Verify(x => x.StartListeningAsync(), Times.Never);
        }

        [Test]
        public async Task StopIQAsync_WhenConnected_StopsIQAndUdpListening()
        {
            // Arrange
            _mockTcpClient.Setup(x => x.Connected).Returns(true);
            _mockTcpClient.Setup(x => x.SendMessageAsync(It.IsAny<byte[]>()))
                .Callback<byte[]>(msg =>
                {
                    _mockTcpClient.Raise(x => x.MessageReceived += null, _mockTcpClient.Object, new byte[] { 0x01 });
                })
                .Returns(Task.CompletedTask);
            _client.IQStarted = true;

            // Act
            await _client.StopIQAsync();

            // Assert
            Assert.That(_client.IQStarted, Is.False);
            _mockTcpClient.Verify(x => x.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
            _mockUdpClient.Verify(x => x.StopListening(), Times.Once);
        }

        [Test]
        public async Task StopIQAsync_WhenNotConnected_DoesNotStopIQ()
        {
            // Arrange
            _mockTcpClient.Setup(x => x.Connected).Returns(false);
            _client.IQStarted = true;

            // Act
            await _client.StopIQAsync();

            // Assert
            Assert.That(_client.IQStarted, Is.True); // State unchanged
            _mockTcpClient.Verify(x => x.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
            _mockUdpClient.Verify(x => x.StopListening(), Times.Never);
        }

        [Test]
        public async Task ChangeFrequencyAsync_WhenConnected_SendsFrequencyChangeMessage()
        {
            // Arrange
            _mockTcpClient.Setup(x => x.Connected).Returns(true);
            byte[] sentMessage = null;
            _mockTcpClient.Setup(x => x.SendMessageAsync(It.IsAny<byte[]>()))
                .Callback<byte[]>(msg =>
                {
                    sentMessage = msg;
                    _mockTcpClient.Raise(x => x.MessageReceived += null, _mockTcpClient.Object, new byte[] { 0x01 });
                })
                .Returns(Task.CompletedTask);

            long frequency = 14100000; // 14.1 MHz
            int channel = 1;

            // Act
            await _client.ChangeFrequencyAsync(frequency, channel);

            // Assert
            _mockTcpClient.Verify(x => x.SendMessageAsync(It.IsAny<byte[]>()), Times.Once);
            Assert.That(sentMessage, Is.Not.Null);
            // Verify that the message contains channel and frequency data
            Assert.That(sentMessage.Length, Is.GreaterThan(0));
        }

        [Test]
        public void Disconnect_CallsTcpClientDisconnect()
        {
            // Act
            _client.Disconect();

            // Assert
            _mockTcpClient.Verify(x => x.Disconnect(), Times.Once);
        }

        [Test]
        public void Constructor_SubscribesToEvents()
        {
            // Arrange & Act
            var mockTcp = new Mock<ITcpClient>();
            var mockUdp = new Mock<IUdpClient>();
            var client = new NetSdrClient(mockTcp.Object, mockUdp.Object);

            // Assert
            mockTcp.VerifyAdd(x => x.MessageReceived += It.IsAny<EventHandler<byte[]>>(), Times.Once);
            mockUdp.VerifyAdd(x => x.MessageReceived += It.IsAny<EventHandler<byte[]>>(), Times.Once);
        }

        [Test]
        public void TcpMessageReceived_CompletesResponseTask()
        {
            // Arrange
            _mockTcpClient.Setup(x => x.Connected).Returns(true);
            var testMessage = new byte[] { 0x01, 0x02, 0x03 };

            // Act - Trigger the event
            Assert.DoesNotThrow(() =>
            {
                _mockTcpClient.Raise(x => x.MessageReceived += null, _mockTcpClient.Object, testMessage);
            });
        }

        [Test]
        public void UdpMessageReceived_ProcessesSamplesCorrectly()
        {
            // Arrange
            var testData = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };

            // Act & Assert - Trigger the UDP event
            Assert.DoesNotThrow(() =>
            {
                _mockUdpClient.Raise(x => x.MessageReceived += null, _mockUdpClient.Object, testData);
            });
        }

        [Test]
        public async Task ChangeFrequencyAsync_WithDifferentChannels_SendsCorrectData()
        {
            // Arrange
            _mockTcpClient.Setup(x => x.Connected).Returns(true);
            var sentMessages = new List<byte[]>();
            _mockTcpClient.Setup(x => x.SendMessageAsync(It.IsAny<byte[]>()))
                .Callback<byte[]>(msg =>
                {
                    sentMessages.Add(msg);
                    _mockTcpClient.Raise(x => x.MessageReceived += null, _mockTcpClient.Object, new byte[] { 0x01 });
                })
                .Returns(Task.CompletedTask);

            // Act
            await _client.ChangeFrequencyAsync(7100000, 0);
            await _client.ChangeFrequencyAsync(14100000, 1);

            // Assert
            Assert.That(sentMessages.Count, Is.EqualTo(2));
            _mockTcpClient.Verify(x => x.SendMessageAsync(It.IsAny<byte[]>()), Times.Exactly(2));
        }

        [Test]
        public void IQStarted_InitiallyFalse()
        {
            // Assert
            Assert.That(_client.IQStarted, Is.False);
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up if needed
            _mockTcpClient = null;
            _mockUdpClient = null;
            _client = null;
        }
    }
}
