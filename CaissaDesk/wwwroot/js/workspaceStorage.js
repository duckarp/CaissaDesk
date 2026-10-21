const STORAGE_KEY = 'caissalytics_workspace_state';

export function saveWorkspaceState(json) {
    try {
        if (json) {
            localStorage.setItem(STORAGE_KEY, json);
        } else {
            localStorage.removeItem(STORAGE_KEY);
        }
    } catch (e) {
        console.warn('Unable to save workspace state to localStorage:', e);
    }
}

export function loadWorkspaceState() {
    try {
        return localStorage.getItem(STORAGE_KEY);
    } catch (e) {
        console.warn('Unable to load workspace state from localStorage:', e);
        return null;
    }
}

export function clearWorkspaceState() {
    try {
        localStorage.removeItem(STORAGE_KEY);
    } catch (e) {
        console.warn('Unable to clear workspace state from localStorage:', e);
    }
}
