import { HubConnection } from '@microsoft/signalr';
import { HubTransport, startHub } from './hub-connection';

function fake(starts: boolean): HubConnection {
  return { start: () => (starts ? Promise.resolve() : Promise.reject(new Error('blocked'))) } as unknown as HubConnection;
}

describe('startHub', () => {
  it('uses a direct WebSocket when it opens', async () => {
    const tried: HubTransport[] = [];
    const wired: HubConnection[] = [];

    const connection = await startHub(
      (transport) => {
        tried.push(transport);
        return fake(true);
      },
      (c) => wired.push(c),
    );

    expect(tried).toEqual(['websocket']);
    expect(wired).toEqual([connection]);
  });

  it('falls back to a negotiated connection when WebSockets are blocked', async () => {
    const tried: HubTransport[] = [];
    const negotiated = fake(true);

    const connection = await startHub(
      (transport) => {
        tried.push(transport);
        return transport === 'websocket' ? fake(false) : negotiated;
      },
      () => undefined,
    );

    expect(tried).toEqual(['websocket', 'negotiated']);
    expect(connection).toBe(negotiated);
  });

  it('fails when neither connects', async () => {
    await expect(startHub(() => fake(false), () => undefined)).rejects.toThrow('blocked');
  });
});
