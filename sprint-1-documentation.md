# Sprint 1 Documentation
## Secure Distributed Messenger

**Team Name:** Group 19

**Team Members:**
- Aveinn Swar - [Role/Responsibilities]
- Matthew Luan - [Role/Responsibilities]
- Aman Shah - [Role/Responsibilities]
- Jordany Roman - [Role/Responsibilities]

**Date:** 2/27/26

---

## Build Instructions

### Prerequisites
- .NET 9.0 SDK or later

### Building the Project
```
dotnet build
```

---

## Run Instructions

### Starting the Application
```
dotnet run
```

### Command Line Arguments (if any)
| Argument | Description | Example |
|----------|-------------|---------|
| | | |

---

## Application Commands

| Command | Description | Example |
|---------|-------------|---------|
| `/connect <ip> <port>` | Connect to a peer | `/connect 192.168.1.100 5000` |
| `/listen <port>` | Start listening for connections | `/listen 5000` |
| `/peers` | List known peers | `/peers` |
| `/history` | View message history | `/history` |
| `/quit` | Exit the application | `/quit` |
| `/exit` | Exit the application | `/exit` |
| | | |

---

## Architecture Overview

### Threading Model
[Describe your threading approach - which threads exist and what each does]

- **Main Thread:** The thread handles the UI, command inputs, and the overall application. It interacts with the MessageQueue to put outgoing messages in the queue and dequeues incoming messages to display them.
- **Receive Thread:** The thread is created for every incoming connection. The thread executes the ReceiveLoop, which blocks and waits for data from peers. The network stream is monitored for incoming messages and continues without blocking other application operations.
- **Send Thread:** The thread writes data to network streams and ensures that sending a message or broadcasting to many peers does not freeze the calling thread.
- **Listen Thread:** The thread monitors for new incoming connection requests.

### Thread-Safe Message Queue
The MessageQueue is implemented as a thread-safe producer-consumer buffer using BlockingCollection<T>. This provides a layer of abstraction for thread safety without requiring manual locks for the primary queue operations. The queue uses BlockingCollection<Message> to handle internal synchronization, ensuring that multiple threads can add to the queue and dequeue messages at the same time without errors.

---

## Features Implemented

- [ ] Multi-threaded architecture
- [ ] Thread-safe message queue
- [ ] TCP server (listen for connections)
- [ ] TCP client (connect to peers)
- [ ] Send/receive text messages
- [ ] Graceful disconnection handling
- [ ] Console UI with commands

---

## Testing Performed

### Test Cases
| Test | Expected Result | Actual Result | Pass/Fail |
|------|-----------------|---------------|-----------|
| Two instances can connect | Connection established | | |
| Messages sent and received | Message appears on other instance | | |
| Disconnection handled | No crash, appropriate message | | |
| Thread safety under load | No race conditions | | |

---

## Known Issues

| Issue | Description | Workaround |
|-------|-------------|------------|
| | | |

---

## Video Demo Checklist

Your demo video (3-5 minutes) should show:
- [ ] Starting two instances of the application
- [ ] Connecting the instances
- [ ] Sending messages in both directions
- [ ] Disconnecting gracefully
- [ ] (Optional) Showing thread-safe behavior under load
