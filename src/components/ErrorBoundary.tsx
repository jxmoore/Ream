import { Component, type ErrorInfo, type ReactNode } from 'react';

interface Props {
  children: ReactNode;
}
interface State {
  error: Error | null;
}

export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    console.error('Unhandled UI error', error, info);
  }

  render() {
    if (this.state.error) {
      return (
        <div className="app-splash">
          <div className="app-splash__logo">Ream</div>
          <p className="app-splash__error">
            Something went wrong.
            <br />
            {this.state.error.message}
          </p>
        </div>
      );
    }
    return this.props.children;
  }
}
