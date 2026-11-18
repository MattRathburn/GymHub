import { HttpTransportType, HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { getCookie } from '../auth/get-cookie';
import { xsrfCookieName, xsrfHeaderName } from '../auth/secure-api.interceptor';

/**
 * - `websocket`: straight to a WebSocket, without the negotiate request. With several API instances and no sticky
 *   sessions, negotiate and connect could land on different instances, so this is the default. (The Gateway checks
 *   the handshake's Origin instead of the XSRF header, which browsers can't send on a WebSocket.)
 * - `negotiated`: the fallback for networks that block WebSockets. SignalR negotiates and may use Server-Sent Events
 *   or long polling; with several API instances that needs session affinity. The negotiate POST carries the XSRF
 *   header like any API call.
 */
export type HubTransport = 'websocket' | 'negotiated';

export function buildHubConnection(url: string, transport: HubTransport): HubConnection {
  const options =
    transport === 'websocket'
      ? { skipNegotiation: true, transport: HttpTransportType.WebSockets, withCredentials: true }
      : { withCredentials: true, headers: { [xsrfHeaderName]: getCookie(xsrfCookieName) } };

  return new HubConnectionBuilder().withUrl(url, options).withAutomaticReconnect().configureLogging(LogLevel.Warning).build();
}

/**
 * Starts a connection from `create`, with its handlers attached by `wire`: a direct WebSocket first, and a negotiated
 * connection if that can't be opened. Returns the one that started; throws if neither did.
 */
export async function startHub(
  create: (transport: HubTransport) => HubConnection,
  wire: (connection: HubConnection) => void,
): Promise<HubConnection> {
  const direct = create('websocket');
  wire(direct);

  try {
    await direct.start();
    return direct;
  } catch (directError) {
    const negotiated = create('negotiated');
    wire(negotiated);

    try {
      await negotiated.start();
      return negotiated;
    } catch {
      throw directError;
    }
  }
}
