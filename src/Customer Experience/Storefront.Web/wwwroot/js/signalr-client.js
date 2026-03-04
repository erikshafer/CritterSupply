// SignalR client for Storefront real-time updates
// Uses @microsoft/signalr library loaded via CDN
window.signalrClient = {
    connection: null,
    dotNetHelper: null,

    /**
     * Subscribe to SignalR hub for a customer
     * @param {string} customerId - Customer GUID
     * @param {DotNetObjectReference} dotNetHelper - .NET object reference for callbacks
     */
    subscribe: async function (customerId, dotNetHelper) {
        // Store dotNetHelper reference
        this.dotNetHelper = dotNetHelper;

        // Close existing connection if any
        if (this.connection) {
            await this.connection.stop();
        }

        // Create SignalR connection
        const url = `http://localhost:5237/hub/storefront?customerId=${customerId}`;

        this.connection = new signalR.HubConnectionBuilder()
            .withUrl(url, {
                transport: signalR.HttpTransportType.WebSockets,
                skipNegotiation: false
            })
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: retryContext => {
                    // Exponential backoff: 0ms, 2s, 10s, 30s, then 30s intervals
                    if (retryContext.previousRetryCount === 0) return 0;
                    if (retryContext.previousRetryCount === 1) return 2000;
                    if (retryContext.previousRetryCount === 2) return 10000;
                    return 30000;
                }
            })
            .configureLogging(signalR.LogLevel.Information)
            .build();

        // Handle incoming messages (Wolverine wraps in CloudEvents)
        this.connection.on('ReceiveMessage', (cloudEvent) => {
            console.log('SignalR CloudEvents message received:', cloudEvent);

            try {
                // CloudEvents structure from Wolverine:
                // {
                //   specversion: "1.0",
                //   type: "CritterSupply.Storefront.RealTime.CartUpdated",
                //   source: "storefront-api",
                //   id: "uuid",
                //   time: "timestamp",
                //   datacontenttype: "application/json",
                //   data: { cartId, customerId, itemCount, totalAmount, occurredAt }
                // }

                // Extract message type from CloudEvents envelope
                const messageType = cloudEvent.type || '';
                const typeName = messageType.split('.').pop(); // Get last part (e.g., "CartUpdated")

                // Map CloudEvents type to legacy SSE eventType for backward compatibility
                let eventType = '';
                if (typeName === 'CartUpdated') {
                    eventType = 'cart-updated';
                } else if (typeName === 'OrderStatusChanged') {
                    eventType = 'order-status-changed';
                } else if (typeName === 'ShipmentStatusChanged') {
                    eventType = 'shipment-status-changed';
                }

                // Unwrap CloudEvents data payload and add eventType discriminator
                const unwrappedEvent = {
                    eventType: eventType,
                    ...cloudEvent.data
                };

                // Invoke .NET callback with unwrapped event data
                this.dotNetHelper.invokeMethodAsync('OnSseEvent', unwrappedEvent);
            } catch (error) {
                console.error('Failed to process SignalR CloudEvents message:', error);
            }
        });

        // Connection lifecycle events
        this.connection.onreconnecting(error => {
            console.warn('SignalR reconnecting...', error);
        });

        this.connection.onreconnected(connectionId => {
            console.log('SignalR reconnected:', connectionId);
        });

        this.connection.onclose(error => {
            console.error('SignalR connection closed:', error);
        });

        // Start connection
        try {
            await this.connection.start();
            console.log('SignalR connection established');
        } catch (error) {
            console.error('Failed to start SignalR connection:', error);
            throw error;
        }
    },

    /**
     * Unsubscribe from SignalR hub
     */
    unsubscribe: async function () {
        if (this.connection) {
            try {
                await this.connection.stop();
                console.log('SignalR connection stopped');
            } catch (error) {
                console.error('Error stopping SignalR connection:', error);
            }
            this.connection = null;
        }
        this.dotNetHelper = null;
    }
};
