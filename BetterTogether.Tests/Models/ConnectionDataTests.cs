using BetterTogether.Extensions;
using BetterTogether.Models;
using MemoryPack;
using System.Collections.Generic;

namespace BetterTogether.Tests.Models
{
    [TestFixture]
    public class ConnectionDataTests
    {
        [SetUp]
        public void SetUp()
        {

        }

        [Test]
        public void DefaultConstructor_InitializesWithDefaultKey()
        {
            // Act
            var connectionData = new ConnectionData();

            Assert.Multiple(() =>
            {
                Assert.That(connectionData.Key, Is.EqualTo(Constants.DEFAULT_KEY));
                Assert.That(connectionData.InitStates, Is.Not.Null);
            });
            Assert.That(connectionData.InitStates, Is.Empty);
        }

        [Test]
        public void ConstructorWithKey_InitializesKeyCorrectly()
        {
            // Arrange
            var customKey = "CustomKey";

            // Act
            var connectionData = new ConnectionData(customKey);

            Assert.Multiple(() =>
            {
                Assert.That(connectionData.Key, Is.EqualTo(customKey));
                Assert.That(connectionData.InitStates, Is.Empty);
            });
        }

        [Test]
        public void ConstructorWithKeyAndInitStates_InitializesPropertiesCorrectly()
        {
            // Arrange
            var customKey = "CustomKey";
            var initStates = new Dictionary<string, byte[]> { { "state1", new byte[] { 1, 2, 3 } } };

            // Act
            var connectionData = new ConnectionData(customKey, initStates);

            Assert.Multiple(() =>
            {
                Assert.That(connectionData.Key, Is.EqualTo(customKey));
                Assert.That(connectionData.InitStates, Is.EqualTo(initStates));
            });
        }

        [Test]
        public void SerializationAndDeserialization_RetainsValues()
        {
            // Arrange
            var customKey = "CustomKey";
            var initStates = new Dictionary<string, byte[]> { { "state1", new byte[] { 1, 2, 3 } } };
            var connectionData = new ConnectionData(customKey, initStates);

            // Act
            var memory = MemoryPackSerializer.Serialize(connectionData);
            var deserialized = MemoryPackSerializer.Deserialize<ConnectionData>(memory);

            Assert.Multiple(() =>
            {
                Assert.That(deserialized.Key, Is.EqualTo(customKey));
                Assert.That(deserialized.InitStates, Is.EqualTo(initStates));
            });
        }

        [Test]
        public void SetState_AddsNewState_WhenKeyDoesNotExist()
        {
            // Arrange
            var connectionData = new ConnectionData();
            var key = "newState";
            var data = new TestMemoryPackable { Value = "TestValue" };

            // Act
            ConnectionDataExtensions.SetState(connectionData, key, data);

            // Assert
            Assert.That(connectionData.InitStates.ContainsKey(key), Is.True);
            var deserializedData = MemoryPackSerializer.Deserialize<TestMemoryPackable>(connectionData.InitStates[key]);
            Assert.That(deserializedData.Value, Is.EqualTo(data.Value));
        }

        [Test]
        public void SetState_UpdatesExistingState_WhenKeyExists()
        {
            // Arrange
            var connectionData = new ConnectionData();
            var key = "existingState";
            var initialData = new TestMemoryPackable { Value = "InitialValue" };
            ConnectionDataExtensions.SetState(connectionData, key, initialData);
            var updatedData = new TestMemoryPackable { Value = "UpdatedValue" };

            // Act
            ConnectionDataExtensions.SetState(connectionData, key, updatedData);

            // Assert
            Assert.That(connectionData.InitStates.ContainsKey(key), Is.True);
            var deserializedData = MemoryPackSerializer.Deserialize<TestMemoryPackable>(connectionData.InitStates[key]);
            Assert.That(deserializedData.Value, Is.EqualTo(updatedData.Value));
        }

        [Test]
        public void DeleteState_RemovesState_WhenKeyExists()
        {
            // Arrange
            var connectionData = new ConnectionData();
            var key = "stateToDelete";
            var data = new TestMemoryPackable { Value = "Value" };
            ConnectionDataExtensions.SetState(connectionData, key, data);

            // Act
            ConnectionDataExtensions.DeleteState(connectionData, key);

            // Assert
            Assert.That(connectionData.InitStates.ContainsKey(key), Is.False);
        }

        [Test]
        public void DeleteState_DoesNothing_WhenKeyDoesNotExist()
        {
            // Arrange
            var connectionData = new ConnectionData();
            var key = "nonExistentState";

            // Act
            ConnectionDataExtensions.DeleteState(connectionData, key);

            // Assert
            Assert.That(connectionData.InitStates.ContainsKey(key), Is.False);
        }

        [TearDown]
        public void TearDown()
        {

        }
    }
}
