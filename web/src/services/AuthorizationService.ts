

import { get, postJson } from "@/utils/fetch"

const baseUrl = "/api/v1/authorization";

export interface ApiResponse<T> {
    success: boolean;
    message: string;
    data: T;
}

export interface BotChallengeConfig {
    enabled: boolean;
    configured: boolean;
    siteKey: string;
}

export const getAdminChallengeConfig = (): Promise<ApiResponse<BotChallengeConfig>> => {
    return get(`${baseUrl}/challenge-config`);
};

export const Auth = (password: string, turnstileToken?: string | null): Promise<ApiResponse<string>> => {
    return postJson(baseUrl, {
        password,
        turnstileToken: turnstileToken ?? null,
    });
};