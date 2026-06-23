(function () {
    class API {

        isLoading = false;
        queryExecutionTime = 0;

        async execute(url, options) {
            // Prepare request
            options = options || {};
            options.method = options.method ?? 'GET';

            // If this is a non-GET request and no explicit body provided, but params exist, send params as JSON body.
            if (options.method !== 'GET' && !options.body && options.params) {
                options.body = JSON.stringify(options.params);
                options.headers = Object.assign({}, options.headers, { 'Content-Type': 'application/json' });
            }

            // Build Query Params for GET requests only
            let fetchUrl = url;
            if (options.method === 'GET' && options.params) {
                const queryParams = new URLSearchParams();
                for (const [key, value] of Object.entries(options.params)) {
                    if (value !== undefined && value !== null) queryParams.append(key, value);
                }
                const qs = queryParams.toString();
                if (qs) fetchUrl = url + `?${qs}`;
            }

            // Before Fetch Callback
            if (options.beforeFetch && typeof options.beforeFetch == 'function') options.beforeFetch();

            // Execute Query (Benchmark it)
            const start = performance.now();
            const response = await fetch(fetchUrl, {
                method: options.method ?? 'GET',
                headers: options.headers ?? undefined,
                body: options.body ?? undefined
            });
            const fetchTimeMs = (performance.now() - start).toFixed(1);

            // If bad response, throw error
            if (!response.ok) throw new Error(await response.text());

            // Get the Formatted response (JSON / Blob)
            let formattedResponse;
            if (options.responseType === 'blob') {
                formattedResponse = await response.blob();
            } else {
                formattedResponse = await response.json();
            }

            // After Fetch Callback
            if (options.afterFetch && typeof options.afterFetch == 'function') options.afterFetch(fetchTimeMs);

            return formattedResponse;
        }

        async get(url, options) {
            return await this.execute(url, options);
        }

        async post(url, options) {
            options.method = 'POST';
            return await this.execute(url, options);
        }

        async put(url, options) {
            options.method = 'PUT';
            return await this.execute(url, options);
        }

        async delete(url, options) {
            options.method = 'DELETE';
            return await this.execute(url, options);
        }
    }

    // Expose to entire webpage
    window.API = API;
})();