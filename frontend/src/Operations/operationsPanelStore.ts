import { create } from 'zustand';

const STORAGE_KEY = 'cinnabarr.operationsPanel';

interface OperationsPanelState {
  isOpen: boolean;
}

const read = (): OperationsPanelState => {
  try {
    return {
      isOpen: false,
      ...JSON.parse(window.localStorage.getItem(STORAGE_KEY) ?? '{}'),
    };
  } catch {
    return { isOpen: false };
  }
};

const write = (value: OperationsPanelState) => {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
  } catch {
    // Remembering the panel's state is a convenience only.
  }
};

// Whether the operations panel is open or minimised to its handle, remembered in this browser.
const operationsPanelStore = create<OperationsPanelState>(read);

export const useIsOperationsPanelOpen = () =>
  operationsPanelStore((state) => state.isOpen);

export const setIsOperationsPanelOpen = (isOpen: boolean) => {
  operationsPanelStore.setState({ isOpen });
  write({ isOpen });
};

export const toggleOperationsPanel = () => {
  setIsOperationsPanelOpen(!operationsPanelStore.getState().isOpen);
};
