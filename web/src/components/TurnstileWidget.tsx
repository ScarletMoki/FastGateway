import { useEffect, useRef } from 'react';

type TurnstileRenderOptions = {
    sitekey: string;
    action?: string;
    callback?: (token: string) => void;
    'expired-callback'?: () => void;
    'error-callback'?: () => void;
};

type TurnstileApi = {
    render: (container: HTMLElement, options: TurnstileRenderOptions) => string;
    remove: (widgetId: string) => void;
};

declare global {
    interface Window {
        turnstile?: TurnstileApi;
    }
}

const SCRIPT_ID = 'fastgateway-turnstile-script';
const SCRIPT_URL = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';

interface TurnstileWidgetProps {
    siteKey: string;
    action?: string;
    onToken: (token: string | null) => void;
    onError?: () => void;
}

let scriptPromise: Promise<void> | null = null;

const loadTurnstile = (): Promise<void> => {
    if (window.turnstile) return Promise.resolve();
    if (scriptPromise) return scriptPromise;

    scriptPromise = new Promise((resolve, reject) => {
        const existing = document.getElementById(SCRIPT_ID) as HTMLScriptElement | null;
        if (existing) {
            existing.addEventListener('load', () => resolve(), { once: true });
            existing.addEventListener('error', () => reject(new Error('Turnstile 脚本加载失败')), { once: true });
            return;
        }

        const script = document.createElement('script');
        script.id = SCRIPT_ID;
        script.src = SCRIPT_URL;
        script.async = true;
        script.defer = true;
        script.onload = () => resolve();
        script.onerror = () => reject(new Error('Turnstile 脚本加载失败'));
        document.head.appendChild(script);
    });

    return scriptPromise;
};

const TurnstileWidget = ({ siteKey, action = 'admin-login', onToken, onError }: TurnstileWidgetProps) => {
    const containerRef = useRef<HTMLDivElement>(null);
    const widgetIdRef = useRef<string | null>(null);
    const onTokenRef = useRef(onToken);
    const onErrorRef = useRef(onError);
    onTokenRef.current = onToken;
    onErrorRef.current = onError;

    useEffect(() => {
        let disposed = false;

        void loadTurnstile()
            .then(() => {
                if (disposed || !containerRef.current || !window.turnstile) return;

                widgetIdRef.current = window.turnstile.render(containerRef.current, {
                    sitekey: siteKey,
                    action,
                    callback: (token) => onTokenRef.current(token),
                    'expired-callback': () => onTokenRef.current(null),
                    'error-callback': () => {
                        onTokenRef.current(null);
                        onErrorRef.current?.();
                    },
                });
            })
            .catch(() => {
                onTokenRef.current(null);
                onErrorRef.current?.();
            });

        return () => {
            disposed = true;
            if (widgetIdRef.current && window.turnstile) {
                window.turnstile.remove(widgetIdRef.current);
                widgetIdRef.current = null;
            }
        };
    }, [action, siteKey]);

    return <div ref={containerRef} aria-label="人机验证" />;
};

export default TurnstileWidget;
