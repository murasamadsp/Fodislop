import sys, json, asyncio, websockets

async def main():
    tool = sys.argv[1]
    args = json.loads(sys.argv[2]) if len(sys.argv) > 2 else {}
    async with websockets.connect("ws://localhost:8090/McpUnity") as ws:
        req = {"jsonrpc": "2.0", "id": 1, "method": tool,
               "params": args}
        await ws.send(json.dumps(req))
        try:
            resp = await asyncio.wait_for(ws.recv(), timeout=45)
            out = json.dumps(json.loads(resp), indent=1)
            if len(sys.argv) > 3:
                open(sys.argv[3], 'w').write(out)
                print("WROTE", sys.argv[3])
            else:
                print(out)
        except asyncio.TimeoutError:
            print("TIMEOUT")

asyncio.run(main())
