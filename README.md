### Features

Base Requirements:
  - ✅ Builds in Visual Studio (I'm specifically using VSCode)
  - ✅ Web API that allows browsing / searching and returns json
  - ✅ Deep Linkable URL Pattern - Allows for sharing URL with friends while also allowing users to refresh without losing state
  - ✅ Single Page Application - Very simple JS implementation, once you're on the page, you stay on it.
  - ✅ Browsing of files and folders
  - ✅ Searching of files and folders (within current directory)
  - ✅ Uploading new files
  - ✅ Creating new folders
  - ✅ Showing file and folder counts of the current view
  - ✅ Plain JS - No frameworks

Bonus Features:
  - ✅ Entire file mangager is within a modal
  - ✅ Move Files & Folders
  - ✅ Delete Files & Folders
  - ✅ Duplicate Files & Folders
  - ✅ Move, Delete, and Duplicate can be done by single files or in bulk
  - ✅ Clickable Breadcrumb for deep folder navigation
  - ✅ Total File Count, not just the count of files directly within the current view
  - ✅ Performance: The API caches the results for its expensive file operations (calculating the size & number of files within a directory)

### Known Issues

**Problem:** If files are updated outside of the application, the cache becomes stale and reports the wrong information

- **Solution A:** Implement a file watcher that finds any file recently modified, created, or deleted and then update cache accordingly.   This step will definitely add overhead cost and may not be necessary depending on its use-case, for example if files are ALWAYS managed through this application, then there is no need to watch externally. 

- **Solution B:** Limit the length of the cache by a time-constraint.   This will ensure that if files / folders are changed outside, metrics will quickly be updated based on the time expiration.   Consideration must then be determined how long that time limit it.  Longer times could result in more data being wrong, while Shorter times would result in more overhead / computing cost.

### Improvements

- Add drag and drop functionality for uploading of files.
- Include a deep search mechanic that would search nested folders for a text string
- Multi-file downloads
- DOM Performance: Do not list all files at once in the DOM.  Implement a virtual scroller that only renders the DOM elements that are actively on the screen and then reuses them as scrolling occurs.   This will be particularly useful when folders exceed hundreds of thousands items.
- Add Loading indicators

### AI Usage:

During this process I used VS Code Copilot (generalized prompts below) and Locally hosted models (Qwen3) for Inline Edit Suggestions.  And Gemini responses when googling various C#/.Net methodologies.  No longer have access to exact prompts, but they as follows:

  - "Using the .net codebase, in the ApiController stub out API methods for CRUD operations."
    - Being new to C# and .Net the exact syntax is something that I was not fluent in.   While I could have easily went to google to accomplish this, I felt the simple use of AI was adequete.   This was able to create the shell of the api with empty functions that accepted GET, POST, DELETE operations.

  - "Within .Net, how do I change routing so that all requests not mapped to an api endpoint fallback and render the frontend application."
    - This directed me to the `program.cs` and how the .Net routing works, this helped change the API to serve the JS code as a SPA.

  - I asked AI about improving the methodology of loading the files and folders within a directory.   It felt like extra unnecessary overhead to look for all folders within a directory and then go back and look at all files within a directory.   
    - Sadly within .Net this is the way that you need to do it.

  - When I got to the point of trying to improve the performance, I knew that I/O operations are always expensive and when trying to recusively do so within large numbers of subfolder, I knew this needed to be optimized.  I determined that caching the metrics would be a good first stab at performance as whenever you navigate down and up the folder tree you're doing those heavy operations over and over again.    So I asked AI:  "Extract my file caching logic out to a helper method and implement caching at each folder level via storaging in a map of sorts".
    - The first iteration of what AI generated, while it worked, was atrocious.  There was heavy duplication of code, poor logic implemented, added extra overhead for Normalizing the path every traversed file / folder.  While there were some fruitful outcomes, such as the use of a ConcurrentDictionary as its map.
  
  - Towards the end of the project, I prompted AI with: "Go through my project and find any dead code that could be removed, or locations that code id duplicated and can be simplified by extracting it out.   Do not make any changes yet, prompt your ideas and let me decide whether to take action or not."
    - This did find a few misc cleanup items and it even identified a bug that was in the code.

### Challenges:

Unfamiliarity with C#.   While i do have some experience in recent years with C/C++, I was plesantly surpised when C# is significantly more like Java than C/C++.   This allowed me to utilize previous knowledge of similar languages and jump in quickly.   Naming conventions are going to take a little bit to get used to (Capitalized Functions), but overall I was happy with the outcome and how much .Net bundles in that helps accelerate development.