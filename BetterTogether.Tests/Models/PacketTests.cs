using BetterTogether.Extensions;
using BetterTogether.Enumerations;
using BetterTogether.Models;
using MemoryPack;

namespace BetterTogether.Tests.Models
{
    [TestFixture]
    public class PacketTests
    {
        [SetUp]
        public void SetUp()
        {

        }

        [Test]
        public void Constructor_InitializesPropertiesCorrectly()
        {
            // Arrange
            var type = PacketType.SetState;
            var target = "server";
            var key = "player1";
            var data = new byte[] { 1, 2, 3 };

            // Act
            var packet = new Packet(type, target, key, data);

            Assert.Multiple(() =>
            {
                Assert.That(packet.Type, Is.EqualTo(type));
                Assert.That(packet.Target, Is.EqualTo(target));
                Assert.That(packet.Key, Is.EqualTo(key));
                Assert.That(packet.Data, Is.EquivalentTo(data));
            });
        }

        [Test]
        public void DefaultConstructor_InitializesWithDefaultValues()
        {
            // Act
            var packet = new Packet();

            Assert.Multiple(() =>
            {
                Assert.That(packet.Type, Is.EqualTo(default(PacketType)));
                Assert.That(packet.Target, Is.EqualTo(""));
                Assert.That(packet.Key, Is.EqualTo(""));
                Assert.That(packet.Data, Is.Empty);
            });
        }

        [Test]
        public void NewMethod_CreatesPacketWithSerializedData()
        {
            // Arrange
            var type = PacketType.SetState;
            var target = "server";
            var key = "player1";
            var testData = new TestMemoryPackable { Value = "Test" };

            // Act
            var packet = Packet.New(type, target, key, testData);

            Assert.Multiple(() =>
            {
                Assert.That(packet.Type, Is.EqualTo(type));
                Assert.That(packet.Target, Is.EqualTo(target));
                Assert.That(packet.Key, Is.EqualTo(key));
            });

            var deserializedData = MemoryPackSerializer.Deserialize<TestMemoryPackable>(packet.Data);
            Assert.That(deserializedData.Value, Is.EqualTo(testData.Value));
        }

        [Test]
        public void GetData_ReturnsDeserializedObject_WhenDataIsNotEmpty()
        {
            // Arrange
            var originalData = new TestMemoryPackable { Value = "TestValue" };
            var packet = new Packet(PacketType.SetState, "server", "key", MemoryPackSerializer.Serialize(originalData));

            // Act
            var deserializedData = packet.GetData<TestMemoryPackable>();

            // Assert
            Assert.That(deserializedData, Is.Not.Null);
            Assert.That(deserializedData.Value, Is.EqualTo(originalData.Value));
        }

        [Test]
        public void GetData_ReturnsNull_WhenDataIsEmpty()
        {
            // Arrange
            var packet = new Packet { Data = new byte[0] };

            // Act
            var deserializedData = packet.GetData<TestMemoryPackable>();

            // Assert
            Assert.That(deserializedData, Is.Null);
        }

        [Test]
        public void SetData_SerializesObjectToPacketData()
        {
            // Arrange
            var packet = new Packet();
            var dataToSerialize = new TestMemoryPackable { Value = "TestValue" };

            // Act
            packet.SetData(dataToSerialize);

            // Assert
            Assert.That(packet.Data, Is.Not.Empty);
            var deserializedData = MemoryPackSerializer.Deserialize<TestMemoryPackable>(packet.Data);
            Assert.That(deserializedData.Value, Is.EqualTo(dataToSerialize.Value));
        }

        [Test]
        public void Pack_SerializesPacketToByteArray()
        {
            // Arrange
            var packet = new Packet(PacketType.SetState, "server", "key", [1, 2, 3 ]);

            // Act
            var serializedPacket = packet.Pack();

            // Assert
            Assert.That(serializedPacket, Is.Not.Empty);
            var deserializedPacket = MemoryPackSerializer.Deserialize<Packet>(serializedPacket);
            Assert.That(deserializedPacket.Type, Is.EqualTo(PacketType.SetState));
            Assert.That(deserializedPacket.Target, Is.EqualTo("server"));
            Assert.That(deserializedPacket.Key, Is.EqualTo("key"));
            Assert.That(deserializedPacket.Data, Is.EquivalentTo(new byte[] { 1, 2, 3 }));
        }

        [TearDown]
        public void TearDown()
        {

        }
    }
}
