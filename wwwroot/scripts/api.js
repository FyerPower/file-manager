(function () {
    class API {

        isLoading = false;
        queryExecutionTime = 0;

        async execute(url, options) {
            // Build Query Params
            const queryParams = new URLSearchParams();
            if (options.params) {
                for (const [key, value] of Object.entries(options.params)) {
                    if (value) queryParams.append(key, value);
                }
            }

            // Before Fetch Callback
            if (options.beforeFetch && typeof options.beforeFetch == 'function') options.beforeFetch();

            // Execute Query (Benchmark it)
            const start = performance.now();
            const response = await fetch(url + `?${queryParams.toString()}`, {
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