(function () {
    const api = new API();

    // Default the currentPath to empty string (root)
    let currentPath = '';
    let currentSearch = '';

    // Using a set instead of a list to force single item selection and simplify add/remove logic
    // This also allows us to select multiple items at once to mass deletion or move.
    const selectedItems = new Set();

    // Get references to all the dom elements
    const $fileListDiv = document.getElementById('fileList');
    const $currentPathDiv = document.getElementById('currentPath');
    const $searchInput = document.getElementById('search');
    const $searchButton = document.getElementById('btnSearch');
    const $createFolderButton = document.getElementById('btnNewFolder');
    const $uploadFileButton = document.getElementById('btnUploadFile');
    const $fileInput = document.getElementById('fileInput');
    const $moveButton = document.getElementById('btnMove');
    const $duplicateButton = document.getElementById('btnDuplicate')
    const $deleteButton = document.getElementById('btnDelete');
    const $backButton = document.getElementById('btnBack');
    const $statFileCount = document.getElementById('statFileCount');
    const $statFolderCount = document.getElementById('statFolderCount');
    const $statTotalSize = document.getElementById('statTotalSize');
    const $queryTimeResults = document.getElementById('queryTimeResults');

    // Attach event listeners for the following events: Search, Create Folder, Upload File, Move, Delete, Back
    $searchInput.addEventListener('keydown', handleSearchInputKeydown);
    $searchButton.addEventListener('click', handleSearchButton);
    $createFolderButton.addEventListener('click', handleNewFolder);
    $uploadFileButton.addEventListener('click', handleUploadButton);
    $fileInput.addEventListener('change', handleFileChange);
    $moveButton.addEventListener('click', handleMoveButton);
    $duplicateButton.addEventListener('click', handleDuplicateButton);
    $deleteButton.addEventListener('click', handleDeleteButton);
    $backButton.addEventListener('click', handleBackButton);

    function getPathFromLocation() {
        let path = window.location.pathname || '/';
        path = path.replace(/\/+$|^\/+$/g, '');
        return path === '' || path === '/' ? '' : path.replace(/^\//, '');
    }

    function normalizePath(path) {
        if (!path) return '';
        return path.replace(/^\/+|\/+$/g, '');
    }

    /**
     * Initialize Application
     * On load, read the current pathname and optional search query from the browser location.
     */
    const initialPath = getPathFromLocation();
    const initialSearch = new URLSearchParams(window.location.search).get('search') ?? '';
    if (initialSearch) {
        $searchInput.value = initialSearch;
    }
    navigateToPath(initialPath, initialSearch, true);

    // Listening to the PopState will allow us to use the back button on the browser
    window.addEventListener('popstate', () => {
        const path = getPathFromLocation();
        const search = new URLSearchParams(window.location.search).get('search') ?? '';
        if (search !== ($searchInput.value || '').trim()) {
            $searchInput.value = search;
        }
        navigateToPath(path, search, true);
    });

    /****************************
     * Functions
     ****************************/

    /**
     * Loads the list of files and folders from the server for a given path and updates the UI accordingly. 
     * It also updates the current path display, manages the selection state, and updates the URL query params for 
     * persistence and sharing.
     * 
     * @param {string} path - The relative path to load (relative to root). If empty or null, loads the root directory.
     */
    async function navigateToPath(path, search, replaceHistory = false) {
        try {
            path = normalizePath(path);
            search = search || '';

            // Load Directory Listing
            // TODO: Add Loading Indicator
            const items = await api.get('/api', {
                params: { search, path },
                beforeFetch: () => { console.log("Loading"); },
                afterFetch: (ms) => { $queryTimeResults.innerHTML = `Latest Fetch Time: ${ms} ms`; }
            });

            // Update the current path to the provided path
            currentPath = path || '';
            currentSearch = search;

            // Render the List
            renderList(items);

            // After Successful Navigation.. we'll want to clear our selected items
            selectedItems.clear();
            updateActionButtons();

            // After Successful Navigation.. update breadcrumbs
            // $currentPathDiv.textContent = currentPath;
            setCurrentPathDisplay();

            // After Successful Navigation.. update the state of the back button
            $backButton.disabled = !currentPath;
            $backButton.classList.toggle('opacity-30', !currentPath);

            // After Successful Navigation.. update the browser URL
            try {
                const params = new URLSearchParams();
                if (currentSearch) params.set('search', currentSearch);
                const pathSegment = currentPath ? `/${currentPath}` : '/';
                const newUrl = pathSegment + (params.toString() ? `?${params.toString()}` : '');
                if (replaceHistory) {
                    history.replaceState(null, '', newUrl);
                } else {
                    history.pushState(null, '', newUrl);
                }
            } catch (e) {
                // If history API isn't available for any reason, silently ignore
            }
        } catch (e) {
            alert('Failed to load directory');
        }
    }

    function setCurrentPathDisplay() {
        try {
            // Clear existing items
            $currentPathDiv.innerHTML = '';

            const breadcrumbLink = document.createElement('a');
            breadcrumbLink.className = 'text-sm cursor-pointer';
            breadcrumbLink.textContent = '🏠';
            breadcrumbLink.href = 'javascript:void(0)';
            breadcrumbLink.onclick = (e) => {
                navigateToPath('');
            };
            $currentPathDiv.appendChild(breadcrumbLink);

            if (currentPath) {
                const breadcrumbs = currentPath.split("/");
                for (let i = 0; i < breadcrumbs.length; i++) {
                    // Add the list item to the list
                    const folderDelimiter = document.createElement('span');
                    folderDelimiter.textContent = '/';
                    $currentPathDiv.appendChild(folderDelimiter);

                    // Add Breadcrumb
                    const breadcrumbLink = document.createElement('a');
                    breadcrumbLink.className = 'text-sm text-blue-600 underline cursor-pointer';
                    breadcrumbLink.textContent = breadcrumbs[i];
                    breadcrumbLink.href = 'javascript:void(0)';
                    breadcrumbLink.onclick = (e) => {
                        navigateToPath(breadcrumbs.slice(0, i + 1).join("/"));
                    };
                    $currentPathDiv.appendChild(breadcrumbLink);
                }
            }
        } catch (e) { alert('Failed to set breadcrumbs'); }
    }

    /**
     * Refresh the file list for the current folder path
     */
    async function refreshFileList() {
        await navigateToPath(currentPath, currentSearch);
    }

    /**
     * On back button click, return up a directory (if possible)
     */
    function handleBackButton() {
        if (!currentPath) return;
        const parts = currentPath.split('/');
        parts.pop();
        const parent = parts.join('/');
        navigateToPath(parent, currentSearch);
    }

    /**
     * Renders the list of files and folders in the UI based on the provided items array. Each item is 
     * displayed with an appropriate icon (folder or file) and includes functionality for selection, 
     * navigation, and downloading (for files).
     */
    function renderList(items) {
        // Clear existing items
        $fileListDiv.innerHTML = '';

        let fileCount = 0, folderCount = 0, totalSize = 0, totalFileCount = 0;

        // Display new Items
        items.forEach(it => {
            // Create List Item
            const li = document.createElement('li');
            li.className = 'flex items-center justify-between p-2 border rounded cursor-pointer select-none';
            li.dataset.name = it.name;
            li.dataset.type = it.type;

            // Left Side: Icon (Unicode) + Name
            const leftContent = document.createElement('div');
            leftContent.className = 'flex items-center gap-3';
            // Icon
            const iconDiv = document.createElement('div');
            iconDiv.textContent = it.type === 'folder' ? '📁' : '📄';
            leftContent.appendChild(iconDiv);
            // Name
            const nameDiv = document.createElement('div');
            if (it.type === 'file') {
                const downloadButton = document.createElement('a');
                downloadButton.className = 'text-sm text-blue-600 underline cursor-pointer';
                downloadButton.textContent = it.name;
                const filePath = (currentPath ? currentPath + '/' : '') + it.name;
                downloadButton.href = 'javascript:void(0)';
                downloadButton.onclick = (e) => {
                    e.preventDefault();
                    e.stopImmediatePropagation();
                    downloadFile(filePath, it.name);
                };
                nameDiv.appendChild(downloadButton);
            } else {
                nameDiv.textContent = it.name;
            }
            leftContent.appendChild(nameDiv);

            // Add left side
            li.appendChild(leftContent);

            // Right Side: Download Button (only for files)
            const rightContent = document.createElement('div');
            rightContent.className = 'flex items-center text-right';
            // File Count
            if (it.type === 'folder') {
                const subFileCount = document.createElement('div');
                subFileCount.textContent = `${it.fileCount.toLocaleString()} Files`;
                subFileCount.className = 'text-sm text-gray-400';
                rightContent.appendChild(subFileCount);
            }
            // Size
            const sizeDiv = document.createElement('div');
            sizeDiv.textContent = formatFileSize(it.size);
            sizeDiv.className = 'text-sm text-gray-400 w-20';
            rightContent.appendChild(sizeDiv);

            // Add right side
            li.appendChild(rightContent);

            // Add single click listener for selection
            li.addEventListener('click', (e) => {
                const key = (currentPath ? currentPath + '/' : '') + it.name;
                if (selectedItems.has(key)) {
                    selectedItems.delete(key);
                    li.classList.remove('bg-blue-100');
                } else {
                    selectedItems.add(key);
                    li.classList.add('bg-blue-100');
                }
                updateActionButtons();
            });

            // Add double click listener for navigation (if folder)
            li.addEventListener('dblclick', (e) => {
                if (it.type === 'folder') {
                    const newPath = (currentPath ? currentPath + '/' : '') + it.name;
                    navigateToPath(newPath, currentSearch);
                }
            });

            // Add the list item to the list
            $fileListDiv.appendChild(li);

            // Incremenet File Statistics
            if (it.type === 'file') fileCount++;
            if (it.type === 'folder') folderCount++;
            totalFileCount += it.fileCount;
            totalSize += it.size;
        });

        $statFileCount.textContent = `${fileCount.toLocaleString()} (${totalFileCount.toLocaleString()})`;
        $statFolderCount.textContent = folderCount.toLocaleString();
        $statTotalSize.textContent = formatFileSize(totalSize);
    }

    async function downloadFile(filePath, fileName) {
        try {
            const blob = await api.post('/api/download', {
                body: JSON.stringify({ path: filePath }),
                headers: { 'Content-Type': 'application/json' },
                responseType: 'blob'
            });

            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            a.remove();
            URL.revokeObjectURL(url);
        } catch (error) {
            alert('Failed to download file: ' + (error.message || error));
        }
    }

    /**
     * Updates the disabled state of the buttons based on whether or not any items are selected
     */
    function updateActionButtons() {
        // Check selection status
        const hasFilesSelected = selectedItems.size > 0;

        // Update State
        $moveButton.disabled = !hasFilesSelected;
        $deleteButton.disabled = !hasFilesSelected;
        $duplicateButton.disabled = !hasFilesSelected;

        // Update Style
        $moveButton.classList.toggle('opacity-30', !hasFilesSelected);
        $deleteButton.classList.toggle('opacity-30', !hasFilesSelected);
        $duplicateButton.classList.toggle('opacity-30', !hasFilesSelected);
    }

    /**
     * Handles changing the search text
     */
    function handleSearchButton() {
        const searchText = $searchInput.value.trim();
        if (searchText === currentSearch) return;

        navigateToPath(currentPath, searchText);
    }

    /**
     * Handle the keydown eventson the Search Input, whenever the user hits enter, perform search
     */
    function handleSearchInputKeydown(event) {
        if (event.key === 'Enter') {
            handleSearchButton();
        }
    }

    /**
     * Handles the creation of a new folder when the "Create Folder" button is clicked. It prompts the user 
     * for a folder name, constructs the appropriate path, and sends a POST request to the server to create 
     * the folder. After successful creation, it refreshes the current directory view.
     */
    async function handleNewFolder() {
        // Keeping it simple.  Standard browser prompt for folder name input.  In a production app, you'd 
        //   likely want a custom modal with better validation and UX.
        const folderName = prompt('Folder name:');

        // If user cancels or enters an empty name, we simply return early and do nothing.
        if (!folderName) return;

        // Construct the path for the new folder. If we're currently in a subdirectory, we need to append 
        //   the new folder name to the current path.
        const folderPath = (currentPath ? currentPath + '/' : '') + folderName + '/';
        try {
            // Send POST request to the server to create the new folder
            await api.post('/api', {
                params: { path: encodeURIComponent(folderPath) },
                beforeFetch: () => { console.log("Duplicating"); },
                afterFetch: (ms) => { console.log("Duplicating", ms); }
            });
            // After successfully creating the folder.. refresh the view.
            refreshFileList();
        } catch (e) {
            const errorMessage = e.body ? await e.text() : e;
            alert(`Failed to create folder.  ${errorMessage}`);
        }
    }

    /**
     * Handles the click event for the "Upload File" button. It programmatically triggers a click on the hidden file input element, 
     * allowing the user to select a file for upload. Once a file is selected, the change event on the file input will be triggered, 
     * which is handled by the handleFileChange function to perform the actual upload.
     */
    function handleUploadButton() {
        $fileInput.click();
    }
    async function handleFileChange(event) {
        // Get file from input
        const input = event.target;
        if (!input.files || input.files.length === 0) return;
        const file = input.files[0];
        // Build form data with the file contents
        const formData = new FormData();
        formData.append('file', file);

        try {
            // Build the full path the file for use on upload
            const path = (currentPath ? currentPath + '/' : '') + file.name;
            // Upload to backend api
            // TODO: Add Loading Indicator
            await api.post('/api', { params: { path }, body: formData });
            // On success, clear the file input and refresh the file list.
            input.value = '';
            refreshFileList();
        } catch (e) {
            const errorMessage = e.body ? await e.text() : e;
            alert(`Failed to upload file.  ${errorMessage}`);
        }
    }

    /**
     * Handle the move button
     */
    async function handleMoveButton() {
        if (selectedItems.size === 0) return alert('Select items to move');
        const dst = prompt('Destination folder (relative to root):');
        if (dst === "/") dst = "";
        try {
            for (const src of Array.from(selectedItems)) {
                const name = src.split('/').pop();
                const destination = dst.endsWith('/') || dst === '' ? dst + name : (dst + '/' + name);
                // TODO: Add Loading Indicator
                await api.put('/api', {
                    params: { sourcePath: src, destinationPath: destination },
                    beforeFetch: () => { console.log("Moving File"); },
                    afterFetch: (ms) => { console.log("Successfully Moved File", ms); }
                });
            }
            selectedItems.clear();
            updateActionButtons();
            refreshFileList();
        } catch (e) { alert('Failed to move'); }
    }

    /**
     * File / Folder Deletion
     */
    async function handleDeleteButton() {
        if (selectedItems.size === 0) return alert('Select items to delete');
        if (!confirm('Delete selected items?')) return;
        try {
            for (const path of Array.from(selectedItems)) {
                // TODO: Add loading indicator
                await api.delete('/api', {
                    params: { path: encodeURIComponent(path) },
                    beforeFetch: () => { console.log("Deleting Object"); },
                    afterFetch: (ms) => { console.log("Successfully Deleted Object", ms); }
                });
                selectedItems.delete(path);
            }
            updateActionButtons();
            refreshFileList();
        } catch (e) { alert('Failed to delete'); }
    }

    /**
     * File / Folder Duplication
     */
    async function handleDuplicateButton() {
        if (selectedItems.size === 0) return alert('Select items to duplicate');
        try {
            for (const path of Array.from(selectedItems)) {
                // TODO: Add Loading Indicator Per Item
                await api.post('/api/duplicate', {
                    params: { path: encodeURIComponent(path) },
                    beforeFetch: () => { console.log("Duplicating"); },
                    afterFetch: (ms) => { console.log("Duplicating", ms); }
                });
                selectedItems.delete(p);
            }
            updateActionButtons();
            refreshFileList();
        } catch (e) {
            const msg = e && e.message ? e.message : String(e);
            alert('Failed to duplicate: ' + msg);
        }
    }

    // Utility method for displaying file size in a readable format, not just raw bytes
    function formatFileSize(bytes) {
        if (bytes === null) {
            return '';
        }

        const units = ['B', 'KB', 'MB', 'GB', 'TB'];
        let size = bytes;
        let unit = 0;

        while (size >= 1024 && unit < units.length - 1) {
            size /= 1024;
            unit++;
        }

        return `${size.toFixed(size < 10 && unit > 0 ? 1 : 0)} ${units[unit]}`;
    }
})();