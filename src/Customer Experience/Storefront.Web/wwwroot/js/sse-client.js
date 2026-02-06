// SSE (Server-Sent Events) client for Storefront real-time updates
window.sseClient = {
    eventSource: null,

    /**
     * Subscribe to SSE stream for a customer
     * @param {string} customerId - Customer GUID
     * @param {DotNetObjectReference} dotNetHelper - .NET object reference for callbacks
     */
    subscribe: function (customerId, dotNetHelper) {
        // Close existing connection if any
        if (this.eventSource) {
            this.eventSource.close();
        }

        // Open new SSE connection
        const url = `http://localhost:5237/sse/storefront?customerId=${customerId}`;
        this.eventSource = new EventSource(url);

        this.eventSource.onopen = function () {
            console.log('SSE connection opened');
        };

        this.eventSource.onmessage = function (event) {
            console.log('SSE event received:', event.data);
            try {
                const data = JSON.parse(event.data);
                // Invoke .NET callback with event data
                dotNetHelper.invokeMethodAsync('OnSseEvent', data);
            } catch (error) {
                console.error('Failed to parse SSE event:', error);
            }
        };

        this.eventSource.onerror = function (error) {
            console.error('SSE connection error:', error);
            // EventSource will automatically attempt to reconnect
        };
    },

    /**
     * Unsubscribe from SSE stream
     */
    unsubscribe: function () {
        if (this.eventSource) {
            this.eventSource.close();
            this.eventSource = null;
            console.log('SSE connection closed');
        }
    }
};
