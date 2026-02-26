// Authentication helper for Blazor
// Uses browser's fetch API to ensure cookies are properly set/sent

window.authHelper = {
    /**
     * Login via server-side endpoint
     * Returns { success: bool, firstName: string } or null on error
     */
    login: async function(email, password) {
        try {
            const response = await fetch('/api/auth/login', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ email, password }),
                credentials: 'same-origin' // Include cookies
            });

            if (response.ok) {
                return await response.json();
            } else if (response.status === 401) {
                return { success: false, firstName: null };
            } else {
                console.error('Login failed:', response.status, response.statusText);
                return null;
            }
        } catch (error) {
            console.error('Login error:', error);
            return null;
        }
    },

    /**
     * Logout via server-side endpoint
     * Returns true on success, false on error
     */
    logout: async function() {
        try {
            const response = await fetch('/api/auth/logout', {
                method: 'POST',
                credentials: 'same-origin' // Include cookies
            });

            // Clear cart ID on logout
            if (response.ok) {
                localStorage.removeItem('cartId');
            }

            return response.ok;
        } catch (error) {
            console.error('Logout error:', error);
            return false;
        }
    },

    /**
     * Store cart ID in localStorage
     */
    setCartId: function(cartId) {
        localStorage.setItem('cartId', cartId);
    },

    /**
     * Get cart ID from localStorage
     * Returns cartId string or null
     */
    getCartId: function() {
        return localStorage.getItem('cartId');
    },

    /**
     * Clear cart ID from localStorage
     */
    clearCartId: function() {
        localStorage.removeItem('cartId');
    }
};
