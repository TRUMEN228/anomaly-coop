# TCP relay that makes a localhost link behave like a laggy internet one (two test games on one PC).
# Each chunk is delivered after a random one-way delay, in order, as TCP would.
# The link's UDP side (engine r20) goes through the same port: each datagram on its own delay, some lost, some
# out of order, as the internet does.
# usage: python lag_relay.py <listen_port> <target_port> [min_ms] [max_ms]
import asyncio, random, sys, time

LISTEN, TARGET = int(sys.argv[1]), int(sys.argv[2])
MIN_MS = int(sys.argv[3]) if len(sys.argv) > 3 else 50
MAX_MS = int(sys.argv[4]) if len(sys.argv) > 4 else 150


async def pipe(reader, writer, name):
    due_last = 0.0
    queue = asyncio.Queue()

    async def deliver():
        while True:
            due, data = await queue.get()
            if data is None:
                break
            wait = due - time.monotonic()
            if wait > 0:
                await asyncio.sleep(wait)
            writer.write(data)
            await writer.drain()
        writer.close()

    task = asyncio.create_task(deliver())
    try:
        while True:
            data = await reader.read(65536)
            if not data:
                break
            # never earlier than the chunk before: a late chunk holds up the ones behind it
            due_last = max(due_last, time.monotonic() + random.uniform(MIN_MS, MAX_MS) / 1000)
            queue.put_nowait((due_last, data))
    except ConnectionError:
        pass
    queue.put_nowait((0, None))
    await task
    print(f"{name} closed", flush=True)


async def handle(client_r, client_w):
    try:
        host_r, host_w = await asyncio.open_connection("127.0.0.1", TARGET)
    except OSError as e:
        print("target unreachable:", e, flush=True)
        client_w.close()
        return
    print(f"link up, delay {MIN_MS}-{MAX_MS} ms each way", flush=True)
    await asyncio.gather(pipe(client_r, host_w, "guest->host"), pipe(host_r, client_w, "host->guest"))


LOSS = 0.02


class ToHost(asyncio.DatagramProtocol):
    # the relay's socket towards the host for one guest address: the host answers here
    def __init__(self, front, guest):
        self.front, self.guest = front, guest

    def connection_made(self, transport):
        self.transport = transport

    def datagram_received(self, data, addr):
        later(lambda: self.front.transport.sendto(data, self.guest))


class Front(asyncio.DatagramProtocol):
    # the guest's datagrams come here (the port the guest reached the host's TCP at)
    def __init__(self):
        self.back = {}

    def connection_made(self, transport):
        self.transport = transport

    def datagram_received(self, data, addr):
        back = self.back.get(addr)
        if back is None:
            loop = asyncio.get_running_loop()
            self.back[addr] = back = ToHost(self, addr)
            loop.create_task(loop.create_datagram_endpoint(lambda: back, remote_addr=("127.0.0.1", TARGET)))
            print("udp side up", flush=True)
        later(lambda: back.transport.sendto(data) if hasattr(back, "transport") else None)


def later(fn):
    if random.random() < LOSS:
        return
    asyncio.get_running_loop().call_later(random.uniform(MIN_MS, MAX_MS) / 1000, fn)


async def main():
    server = await asyncio.start_server(handle, "127.0.0.1", LISTEN)
    await asyncio.get_running_loop().create_datagram_endpoint(Front, local_addr=("127.0.0.1", LISTEN))
    print(f"relay 127.0.0.1:{LISTEN} -> :{TARGET} (tcp and udp)", flush=True)
    async with server:
        await server.serve_forever()

asyncio.run(main())
