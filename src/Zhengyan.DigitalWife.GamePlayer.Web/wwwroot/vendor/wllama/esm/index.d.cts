import * as util from 'util';

interface GlueMsgError {
    _name: "erro_evt";
    message: string;
}
interface GlueMsgLoadReq {
    _name: "load_req";
    model_paths: string[];
    mmproj_path?: string | undefined;
    n_ctx_auto: boolean;
    use_mmap: boolean;
    use_mlock: boolean;
    n_gpu_layers: number;
    n_ctx: number;
    n_threads: number;
    model_alias?: string | undefined;
    log_level?: number | undefined;
    embeddings?: boolean | undefined;
    offload_kqv?: boolean | undefined;
    n_batch?: number | undefined;
    n_ubatch?: number | undefined;
    n_parallel?: number | undefined;
    pooling_type?: string | undefined;
    rope_scaling_type?: string | undefined;
    rope_freq_base?: number | undefined;
    rope_freq_scale?: number | undefined;
    yarn_ext_factor?: number | undefined;
    yarn_attn_factor?: number | undefined;
    yarn_beta_fast?: number | undefined;
    yarn_beta_slow?: number | undefined;
    yarn_orig_ctx?: number | undefined;
    cache_type_k?: string | undefined;
    cache_type_v?: string | undefined;
    kv_unified?: boolean | undefined;
    flash_attn?: boolean | undefined;
    swa_full?: boolean | undefined;
    n_ctx_checkpoints?: number | undefined;
    checkpoint_min_step?: number | undefined;
    chat_template?: string | undefined;
    jinja?: boolean | undefined;
    default_template_kwargs_keys?: string[] | undefined;
    default_template_kwargs_vals?: string[] | undefined;
    reasoning?: boolean | undefined;
    image_min_tokens?: number | undefined;
    image_max_tokens?: number | undefined;
    warmup?: boolean | undefined;
    no_kv_offload?: boolean | undefined;
    mmproj_offload?: boolean | undefined;
    cont_batching?: boolean | undefined;
    n_keep?: number | undefined;
    ctx_shift?: boolean | undefined;
    cache_idle_slots?: boolean | undefined;
    n_cache_reuse?: number | undefined;
    lora_paths?: string[] | undefined;
    lora_scales?: number[] | undefined;
    lora_init_without_apply?: boolean | undefined;
    spec_draft_model?: string | undefined;
    spec_draft_ngl?: number | undefined;
    spec_draft_n_max?: number | undefined;
    spec_draft_n_min?: number | undefined;
    spec_draft_p_min?: number | undefined;
    spec_draft_threads?: number | undefined;
    spec_draft_threads_batch?: number | undefined;
    kv_overrides_keys?: string[] | undefined;
    kv_overrides_vals?: string[] | undefined;
    reasoning_budget_tokens?: number | undefined;
    reasoning_budget_message?: string | undefined;
    reasoning_format?: string | undefined;
    skip_chat_parsing?: boolean | undefined;
    prefill_assistant?: boolean | undefined;
}
interface GlueMsgLoadRes {
    _name: "load_res";
    success: boolean;
    n_ctx: number;
    n_batch: number;
    n_ubatch: number;
    n_vocab: number;
    n_ctx_train: number;
    n_embd: number;
    n_layer: number;
    metadata_key: string[];
    metadata_val: string[];
    token_bos: number;
    token_eos: number;
    token_eot: number;
    list_tokens_eog: number[];
    add_bos_token: boolean;
    add_eos_token: boolean;
    has_encoder: boolean;
    token_decoder_start: number;
    media_marker: string;
    has_image_input: boolean;
    has_audio_input: boolean;
    has_audio_output: boolean;
    has_chat: boolean;
    has_audio_stream: boolean;
    has_live_asr: boolean;
    audio_input_rate: number;
    audio_output_rate: number;
    audio_voices: string[];
}
interface GlueMsgCompletionReq {
    _name: "cmpl_req";
    is_chat: boolean;
    is_asr: boolean;
    data_json: string;
    files: Uint8Array[];
}
interface GlueMsgCompletionRes {
    _name: "cmpl_res";
    success: boolean;
    req_id: number;
}
interface GlueMsgEmbeddingReq {
    _name: "embd_req";
    data_json: string;
    files: Uint8Array[];
}
interface GlueMsgEmbeddingRes {
    _name: "embd_res";
    success: boolean;
    message: string;
    req_id: number;
}
interface GlueMsgRerankReq {
    _name: "rrnk_req";
    data_json: string;
}
interface GlueMsgRerankRes {
    _name: "rrnk_res";
    success: boolean;
    req_id: number;
}
interface GlueMsgSystemoneReq {
    _name: "sys1_req";
    data_json: string;
}
interface GlueMsgSystemoneRes {
    _name: "sys1_res";
    success: boolean;
    req_id: number;
}
interface GlueMsgTtsReq {
    _name: "tts__req";
    data_json: string;
    files: Uint8Array[];
}
interface GlueMsgTtsRes {
    _name: "tts__res";
    success: boolean;
    message: string;
    req_id: number;
}
interface GlueMsgRealtimeStartReq {
    _name: "rtst_req";
    data_json: string;
}
interface GlueMsgRealtimeStartRes {
    _name: "rtst_res";
    success: boolean;
    message: string;
    req_id: number;
}
interface GlueMsgRealtimeSendReq {
    _name: "rtsd_req";
    req_id: number;
    data_json: string;
}
interface GlueMsgRealtimeSendRes {
    _name: "rtsd_res";
    success: boolean;
}
interface GlueMsgGetResultReq {
    _name: "gres_req";
    req_id: number;
}
interface GlueMsgGetResultRes {
    _name: "gres_res";
    success: boolean;
    has_more: boolean;
    is_error: boolean;
    data_json: string;
    data_raw: Uint8Array;
}
interface GlueMsgCancelReq {
    _name: "cncl_req";
    req_id: number;
}
interface GlueMsgCancelRes {
    _name: "cncl_res";
    success: boolean;
}
interface GlueMsgTestBackendOpsReq {
    _name: "tbop_req";
    args: string[];
}
interface GlueMsgTestBackendOpsRes {
    _name: "tbop_res";
    retcode: number;
    success: boolean;
}
type GlueMsg = GlueMsgError | GlueMsgLoadReq | GlueMsgLoadRes | GlueMsgCompletionReq | GlueMsgCompletionRes | GlueMsgEmbeddingReq | GlueMsgEmbeddingRes | GlueMsgRerankReq | GlueMsgRerankRes | GlueMsgSystemoneReq | GlueMsgSystemoneRes | GlueMsgTtsReq | GlueMsgTtsRes | GlueMsgRealtimeStartReq | GlueMsgRealtimeStartRes | GlueMsgRealtimeSendReq | GlueMsgRealtimeSendRes | GlueMsgGetResultReq | GlueMsgGetResultRes | GlueMsgCancelReq | GlueMsgCancelRes | GlueMsgTestBackendOpsReq | GlueMsgTestBackendOpsRes;

/**
 * Module code will be copied into worker.
 *
 * Messages between main <==> worker:
 *
 * From main thread to worker:
 * - Send direction: { verb, args, callbackId }
 * - Result direction: { callbackId, result } or { callbackId, err }
 *
 * Signal from worker to main:
 * - Unidirection: { verb, args }
 */

interface Logger {
    debug: typeof console.debug;
    log: typeof console.log;
    warn: typeof console.warn;
    error: typeof console.error;
}
interface TaskParam {
    verb: 'module.init' | 'fs.alloc' | 'fs.write' | 'fs.read_res' | 'wllama.start' | 'wllama.action' | 'wllama.exit' | 'wllama.debug';
    args: any[];
    callbackId: number;
}
interface Task {
    resolve: any;
    reject: any;
    param: TaskParam;
    buffers?: ArrayBuffer[] | undefined;
}
interface WllamaWorkerResources {
    wasmPath: string;
    jsPath?: string | {
        code: string;
    } | undefined;
    compat: boolean;
    noWebGPU?: boolean;
}
declare class ProxyToWorker {
    resources: WllamaWorkerResources;
    logger: Logger;
    suppressNativeLog: boolean;
    taskQueue: Task[];
    taskId: number;
    resultQueue: Task[];
    busy: boolean;
    private exited;
    worker?: Worker | undefined;
    multiThread: boolean;
    nbThread: number;
    useAsyncFile: boolean;
    fileBlobs: Map<string, Blob>;
    constructor(resources: WllamaWorkerResources, nbThread: number, suppressNativeLog: boolean, logger: Logger);
    getModuleCode(): Promise<string>;
    moduleInit(ggufFiles: {
        name: string;
        blob: Blob;
    }[]): Promise<void>;
    wllamaStart(): Promise<number>;
    wllamaAction<T extends GlueMsg>(name: string, body: GlueMsg): Promise<T>;
    wllamaExit(): Promise<void>;
    wllamaDebug(): Promise<any>;
    /**
     * Allocate a new file in heapfs
     * @returns fileId, to be used by fileWrite()
     */
    private fileAlloc;
    /**
     * Write a Blob to heapfs
     */
    private fileWrite;
    private fileReadResponse;
    /**
     * Parse JSON result returned by cpp code.
     * Throw new Error if "__exception" is present in the response
     *
     * TODO: get rid of this function once everything is migrated to Glue
     */
    private parseResult;
    /**
     * Push a new task to taskQueue
     */
    private pushTask;
    /**
     * Main loop for processing tasks
     */
    private runTaskLoop;
    /**
     * Handle messages from worker
     */
    private onRecvMsg;
    private abort;
    private rejectAll;
}

interface ModelSource {
    /**
     * URL to the GGUF file. If the model is splitted, pass the URL to the first shard. Mmproj file should also be provided via this array if you want to use multimodal.
     */
    url: string;
    mmprojUrl?: string;
}
/**
 * Callback function to track download progress
 */
type DownloadProgressCallback = (opts: {
    /**
     * Number of bytes loaded (sum of all shards)
     */
    loaded: number;
    /**
     * Total number of bytes (sum of all shards)
     */
    total: number;
}) => any;
/**
 * Status of the model validation
 */
declare enum ModelValidationStatus {
    VALID = "valid",
    INVALID = "invalid",
    DELETED = "deleted"
}
/**
 * Parameters for ModelManager constructor
 */
interface ModelManagerParams {
    cacheManager?: CacheManager;
    logger?: WllamaLogger;
    /**
     * Number of parallel downloads
     *
     * Default: 3
     */
    parallelDownloads?: number | undefined;
    /**
     * Allow offline mode
     *
     * Default: false
     */
    allowOffline?: boolean | undefined;
}
/**
 * Model class
 *
 * One model can have multiple shards, each shard is a GGUF file.
 */
declare class Model {
    private modelManager;
    constructor(modelManager: ModelManager, url: string, mmprojUrl?: string, savedFiles?: CacheEntry[]);
    /**
     * URL to the GGUF file (in case it contains multiple shards, the URL should point to the first shard)
     *
     * This URL will be used to identify the model in the cache. There can't be 2 models with the same URL.
     */
    url: string;
    /**
     * URL to mmproj file, if exists
     */
    mmprojUrl?: string | undefined;
    /**
     * Size in bytes (total size of all shards).
     *
     * A value of -1 means the model is deleted from the cache. You must call `ModelManager.downloadModel` to re-download the model.
     */
    size: number;
    /**
     * List of all shards in the cache, sorted by original URL (ascending order)
     */
    files: CacheEntry[];
    /**
     * Open and get a list of all shards as Blobs
     */
    open(): Promise<Blob[]>;
    /**
     * Validate the model files.
     *
     * If the model is invalid, the model manager will not be able to use it. You must call `refresh` to re-download the model.
     *
     * Cases that model is invalid:
     * - The model is deleted from the cache
     * - The model files are missing (or the download is interrupted)
     */
    validate(): ModelValidationStatus;
    /**
     * In case the model is invalid, call this function to re-download the model
     */
    refresh(options?: DownloadOptions): Promise<void>;
    /**
     * Remove the model from the cache
     */
    remove(): Promise<void>;
    /**
     * URLs of all files of this model (shards + mmproj), without duplicates
     */
    private getAllUrls;
    private getAllFiles;
    private getTotalDownloadSize;
}
declare class ModelManager {
    cacheManager: CacheManager;
    params: ModelManagerParams;
    logger: WllamaLogger;
    constructor(params?: ModelManagerParams);
    /**
     * Parses a model URL and returns an array of URLs based on the following patterns:
     * - If the input URL is an array, it returns the array itself.
     * - If the input URL is a string in the `gguf-split` format, it returns an array containing the URL of each shard in ascending order.
     * - Otherwise, it returns an array containing the input URL as a single element array.
     * @param modelUrl URL or list of URLs
     */
    static parseModelUrl(modelUrl: string | string[]): string[];
    /**
     * Get all models in the cache
     */
    getModels(opts?: {
        includeInvalid?: boolean;
    }): Promise<Model[]>;
    /**
     * Download a model from the given URL.
     *
     * The URL must end with `.gguf`
     */
    downloadModel(sourceOrURL: ModelSource | string, options?: DownloadOptions): Promise<Model>;
    /**
     * Get a model from the cache or download it if it's not available.
     */
    getModelOrDownload(source: ModelSource, options?: DownloadOptions): Promise<Model>;
    /**
     * Remove all models from the cache
     */
    clear(): Promise<void>;
}

interface StorageFileHint {
    sha256: string;
}
interface StorageBackend {
    isSupported(): boolean;
    /**
     * Read a file from storage by key.
     * @returns Blob, or null if the key does not exist
     */
    read(key: string, hint?: StorageFileHint): Promise<Blob | null>;
    /**
     * Write a ReadableStream to storage under the given key.
     * Overwrites any existing content for that key.
     */
    write(key: string, stream: ReadableStream, hint?: StorageFileHint): Promise<void>;
    /**
     * Get the stored size of a file in bytes.
     * @returns number of bytes, or -1 if the key does not exist
     */
    getSize(key: string, hint?: StorageFileHint): Promise<number>;
    /**
     * List all keys currently in storage.
     * Includes metadata keys - callers are responsible for filtering.
     */
    list(): Promise<Array<{
        key: string;
        size: number;
    }>>;
    /**
     * Delete a single entry by key. No-op if the key does not exist.
     */
    delete(key: string): Promise<void>;
}

type DownloadOptions = {
    /**
     * Callback function to track download progress
     */
    progressCallback?: DownloadProgressCallback;
    /**
     * Additional metadata to be stored with the downloaded file
     */
    metadataAdditional?: Record<string, any>;
    /**
     * Custom headers for the request. Useful for authentication (e.g. Bearer token)
     */
    headers?: Record<string, string>;
    /**
     * Abort signal for the request
     */
    signal?: AbortSignal;
};
declare const POLYFILL_ETAG = "polyfill_for_older_version";
interface CacheEntry {
    /**
     * Storage key for this file, in the format: `${hashSHA1(fullURL)}_${fileName}`
     */
    name: string;
    /**
     * Size of file (in bytes)
     */
    size: number;
    /**
     * Other metadata
     */
    metadata: CacheEntryMetadata;
}
interface CacheEntryMetadata {
    /**
     * ETag header from remote request
     * https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/ETag
     */
    etag: string;
    /**
     * Remote file size (in bytes), used for integrity check
     */
    originalSize: number;
    /**
     * Original URL of the remote model. Unused for now
     */
    originalURL: string;
    /**
     * URL to mmproj file, if exists
     */
    mmprojURL?: string | undefined;
    /**
     * Optional SHA256, mostly used by COS backend
     */
    sha256?: string | undefined;
}
/**
 * Manages cached model files, backed by a pluggable StorageBackend.
 *
 * Defaults to OPFS (Origin Private File System).
 */
declare class CacheManager {
    private sb;
    /**
     * @param backends Array of storage backends to use, in order of preference ; if first is available, use it, otherwise try the next one.
     */
    constructor(backends?: StorageBackend[]);
    /**
     * Convert a given URL into a storage key.
     *
     * Format: `${hashSHA1(fullURL)}_${fileName}`
     */
    getNameFromURL(url: string): Promise<string>;
    /**
     * @deprecated Use `download()` instead
     *
     * Write a new file to cache. This will overwrite existing file.
     *
     * @param name The file name returned by `getNameFromURL()` or `list()`
     */
    write(name: string, stream: ReadableStream, metadata: CacheEntryMetadata): Promise<void>;
    download(url: string, options?: DownloadOptions): Promise<void>;
    /**
     * Open a file in cache for reading
     *
     * @param nameOrURL The file name returned by `getNameFromURL()` or `list()`, or the original URL of the remote file
     * @returns Blob, or null if file does not exist
     */
    open(nameOrURL: string): Promise<Blob | null>;
    /**
     * Get the size of a file in stored cache
     *
     * NOTE: in case the download is stopped mid-way (i.e. user close browser tab), the file maybe corrupted, size maybe different from `metadata.originalSize`
     *
     * @param name The file name returned by `getNameFromURL()` or `list()`
     * @returns number of bytes, or -1 if file does not exist
     */
    getSize(name: string): Promise<number>;
    /**
     * Get metadata of a cached file
     */
    getMetadata(name: string): Promise<CacheEntryMetadata | null>;
    /**
     * Same as `getMetadata()`, but without polyfill. Returns null if the file has no metadata.
     */
    private readMetadata;
    /**
     * List all files currently in cache
     */
    list(): Promise<CacheEntry[]>;
    /**
     * Clear all files currently in cache
     */
    clear(): Promise<void>;
    /**
     * Delete a single file in cache
     *
     * @param nameOrURL Can be either an URL or a name returned by `getNameFromURL()` or `list()`
     */
    delete(nameOrURL: string): Promise<void>;
    /**
     * Delete multiple files in cache.
     *
     * @param predicate A predicate like `array.filter(item => boolean)`
     */
    deleteMany(predicate: (e: CacheEntry) => boolean): Promise<void>;
    /**
     * Write the metadata of the file to disk.
     */
    writeMetadata(name: string, metadata: CacheEntryMetadata): Promise<void>;
}

interface LoadModelParams {
    log_level?: LogLevel;
    seed?: number;
    n_ctx?: number;
    n_batch?: number;
    n_ubatch?: number;
    n_gpu_layers?: number;
    n_threads?: number;
    embeddings?: boolean;
    offload_kqv?: boolean;
    n_parallel?: number;
    kv_unified?: boolean;
    pooling_type?: 'LLAMA_POOLING_TYPE_UNSPECIFIED' | 'LLAMA_POOLING_TYPE_NONE' | 'LLAMA_POOLING_TYPE_MEAN' | 'LLAMA_POOLING_TYPE_CLS' | 'unspecified' | 'none' | 'mean' | 'cls' | 'last' | 'rank';
    rope_scaling_type?: 'LLAMA_ROPE_SCALING_TYPE_UNSPECIFIED' | 'LLAMA_ROPE_SCALING_TYPE_NONE' | 'LLAMA_ROPE_SCALING_TYPE_LINEAR' | 'LLAMA_ROPE_SCALING_TYPE_YARN';
    rope_freq_base?: number;
    rope_freq_scale?: number;
    yarn_ext_factor?: number;
    yarn_attn_factor?: number;
    yarn_beta_fast?: number;
    yarn_beta_slow?: number;
    yarn_orig_ctx?: number;
    cache_type_k?: 'f32' | 'f16' | 'q8_0' | 'q5_1' | 'q5_0' | 'q4_1' | 'q4_0';
    cache_type_v?: 'f32' | 'f16' | 'q8_0' | 'q5_1' | 'q5_0' | 'q4_1' | 'q4_0';
    flash_attn?: boolean;
    swa_full?: boolean;
    chat_template?: string;
    jinja?: boolean;
    reasoning?: boolean;
    image_min_tokens?: number;
    image_max_tokens?: number;
    warmup?: boolean;
    no_kv_offload?: boolean;
    mmproj_offload?: boolean;
    cont_batching?: boolean;
    n_keep?: number;
    ctx_shift?: boolean;
    cache_idle_slots?: boolean;
    n_cache_reuse?: number;
    lora_adapters?: {
        path: string;
        scale?: number;
    }[];
    lora_init_without_apply?: boolean;
    spec_draft_model?: string;
    spec_draft_ngl?: number;
    spec_draft_n_max?: number;
    spec_draft_n_min?: number;
    spec_draft_p_min?: number;
    spec_draft_threads?: number;
    spec_draft_threads_batch?: number;
    kv_overrides?: Record<string, string>;
    reasoning_budget_tokens?: number;
    reasoning_budget_message?: string;
    reasoning_format?: 'none' | 'deepseek-legacy' | 'deepseek';
    skip_chat_parsing?: boolean;
    prefill_assistant?: boolean;
    default_template_kwargs?: Record<string, any>;
}
interface LoadedContextInfo {
    n_vocab: number;
    n_ctx: number;
    n_batch: number;
    n_ubatch: number;
    n_ctx_train: number;
    n_embd: number;
    n_layer: number;
    metadata: Record<string, string>;
    token_bos: number;
    token_eos: number;
    token_eot: number;
    list_tokens_eog: number[];
    has_encoder: boolean;
    token_decoder_start: number;
    add_bos_token: boolean;
    add_eos_token: boolean;
    has_image_input: boolean;
    has_audio_input: boolean;
    has_audio_output: boolean;
    has_chat: boolean;
    has_audio_stream: boolean;
    has_live_asr: boolean;
    audio_input_rate: number;
    audio_output_rate: number;
    audio_voices: string[];
}
interface SamplingParams {
    seed?: number;
    mirostat?: number | undefined;
    mirostat_eta?: number | undefined;
    mirostat_tau?: number | undefined;
    samplers_sequence?: string[] | undefined;
    temp?: number | undefined;
    top_p?: number | undefined;
    top_k?: number | undefined;
    penalty_last_n?: number | undefined;
    penalty_repeat?: number | undefined;
    penalty_freq?: number | undefined;
    penalty_present?: number | undefined;
    dynatemp_range?: number | undefined;
    dynatemp_exponent?: number | undefined;
    grammar?: string;
    n_prev?: number | undefined;
    n_probs?: number | undefined;
    min_p?: number | undefined;
    typ_p?: number | undefined;
    typical_p?: number | undefined;
    logit_bias?: {
        token: number;
        bias: number;
    }[] | undefined;
    ignore_eos?: boolean | undefined;
}
interface StreamParams<T> {
    stream: true;
    onData: (data: T) => void;
}
declare enum LogLevel {
    DEBUG = 1,
    INFO = 2,
    WARN = 3,
    ERROR = 4
}

interface ChatCompletionMessageText {
    type: 'text';
    text: string;
}
interface ChatCompletionMessageImage {
    type: 'image';
    data: ArrayBuffer;
}
interface ChatCompletionMessageAudio {
    type: 'audio';
    data: ArrayBuffer;
}
type ChatCompletionMessageContent = ChatCompletionMessageText | ChatCompletionMessageImage | ChatCompletionMessageAudio;
interface ChatCompletionToolFunctionParameters {
    type: 'object';
    properties: Record<string, {
        type: string;
        description?: string;
        enum?: string[];
        [key: string]: unknown;
    }>;
    required?: string[];
    additionalProperties?: boolean;
}
interface ChatCompletionToolFunction {
    name: string;
    description?: string;
    parameters?: ChatCompletionToolFunctionParameters;
    strict?: boolean;
}
interface ChatCompletionTool {
    type: 'function';
    function: ChatCompletionToolFunction;
}
type ChatCompletionToolChoice = 'none' | 'auto' | 'required' | {
    type: 'function';
    function: {
        name: string;
    };
};
interface ChatCompletionSystemMessage {
    role: 'system';
    content: string;
    name?: string;
}
interface ChatCompletionUserMessage {
    role: 'user';
    content: string | ChatCompletionMessageContent[];
    name?: string;
}
interface ChatCompletionToolCall {
    id: string;
    type: 'function';
    function: {
        name: string;
        arguments: string;
    };
}
interface ChatCompletionAssistantMessage {
    role: 'assistant';
    content?: string | null;
    name?: string;
    tool_calls?: ChatCompletionToolCall[];
}
interface ChatCompletionToolMessage {
    role: 'tool';
    content: string;
    tool_call_id: string;
}
type ChatCompletionMessage = ChatCompletionSystemMessage | ChatCompletionUserMessage | ChatCompletionAssistantMessage | ChatCompletionToolMessage;
type ChatCompletionParams = {
    messages: ChatCompletionMessage[];
    stream?: boolean;
    model?: string;
    abortSignal?: AbortSignal;
    temperature?: number;
    max_tokens?: number;
    logprobs?: boolean;
    top_logprobs?: number;
    logit_bias?: Record<string, number>;
    tools?: ChatCompletionTool[];
    tool_choice?: ChatCompletionToolChoice;
    response_format?: {
        type: 'text' | 'json_object' | 'json_schema';
        json_schema?: {
            name: string;
            schema: unknown;
            strict?: boolean;
        };
    };
    user?: string;
    chat_template_kwargs?: Record<string, any>;
    cache_prompt?: boolean;
    return_tokens?: boolean;
    timings_per_token?: boolean;
    return_progress?: boolean;
} & SamplingParams;
interface ChatCompletionLogprob {
    token: string;
    logprob: number;
    bytes: number[] | null;
}
interface ChatCompletionLogprobsContent extends ChatCompletionLogprob {
    top_logprobs: ChatCompletionLogprob[];
}
interface ChatCompletionChoiceLogprobs {
    content: ChatCompletionLogprobsContent[] | null;
    refusal: ChatCompletionLogprobsContent[] | null;
}
interface ChatCompletionChoice {
    index: number;
    message: ChatCompletionAssistantMessage;
    finish_reason: 'stop' | 'length' | 'tool_calls' | 'content_filter' | null;
    logprobs: ChatCompletionChoiceLogprobs | null;
}
interface ChatCompletionUsage {
    prompt_tokens: number;
    completion_tokens: number;
    total_tokens: number;
    prompt_tokens_details?: {
        cached_tokens: number;
        audio_tokens: number;
    };
    completion_tokens_details?: {
        reasoning_tokens: number;
        audio_tokens: number;
        accepted_prediction_tokens: number;
        rejected_prediction_tokens: number;
    };
}
/** Response when stream=false (or omitted) */
interface ChatCompletionResponse {
    id: string;
    object: 'chat.completion';
    created: number;
    model: string;
    choices: ChatCompletionChoice[];
    usage: ChatCompletionUsage;
    system_fingerprint?: string;
    service_tier?: string;
}
interface ChatCompletionChunkDelta {
    role?: 'assistant';
    content?: string | null;
    tool_calls?: Array<{
        index: number;
        id?: string;
        type?: 'function';
        function?: {
            name?: string;
            arguments?: string;
        };
    }>;
    refusal?: string | null;
}
interface ChatCompletionChunkChoice {
    index: number;
    delta: ChatCompletionChunkDelta;
    finish_reason: 'stop' | 'length' | 'tool_calls' | 'content_filter' | null;
    logprobs: ChatCompletionChoiceLogprobs | null;
}
interface ChatMessagePromptProgress {
    total: number;
    cache: number;
    processed: number;
    time_ms: number;
}
interface ResultTimings {
    cache_n: number;
    prompt_n: number;
    prompt_ms: number;
    prompt_per_token_ms: number;
    prompt_per_second: number;
    predicted_n: number;
    predicted_ms: number;
    predicted_per_token_ms: number;
    predicted_per_second: number;
}
/** Response when stream=true — one chunk per SSE event */
interface ChatCompletionChunk {
    id: string;
    object: 'chat.completion.chunk';
    created: number;
    model: string;
    choices: ChatCompletionChunkChoice[];
    usage?: ChatCompletionUsage | null;
    timings?: ResultTimings;
    prompt_progress?: ChatMessagePromptProgress;
}
type RawCompletionParams = {
    prompt: string | string[];
    stream?: boolean;
    model?: string;
    abortSignal?: AbortSignal;
    suffix?: string;
    max_tokens?: number;
    temperature?: number;
    top_p?: number;
    n?: number;
    logprobs?: number | null;
    echo?: boolean;
    stop?: string | string[];
    presence_penalty?: number;
    frequency_penalty?: number;
    best_of?: number;
    logit_bias?: Record<string, number>;
    seed?: number;
    user?: string;
    return_progress?: boolean;
} & SamplingParams;
interface RawCompletionChoice {
    text: string;
    index: number;
    finish_reason: 'stop' | 'length' | 'content_filter' | null;
    logprobs: {
        tokens: string[];
        token_logprobs: number[];
        top_logprobs: Array<Record<string, number>>;
        text_offset: number[];
    } | null;
}
/** Response when stream=false */
interface RawCompletionResponse {
    id: string;
    object: 'text_completion';
    created: number;
    model: string;
    choices: RawCompletionChoice[];
    usage: ChatCompletionUsage;
    system_fingerprint?: string;
    timings?: ResultTimings;
}
/** One chunk when stream=true */
interface RawCompletionChunk {
    id: string;
    object: 'text_completion';
    created: number;
    model: string;
    choices: Array<{
        text: string;
        index: number;
        finish_reason: 'stop' | 'length' | 'content_filter' | null;
        logprobs: null;
    }>;
    usage?: ChatCompletionUsage | null;
    timings?: ResultTimings;
    prompt_progress?: ChatMessagePromptProgress;
}
type EmbeddingContent = ChatCompletionMessageText | ChatCompletionMessageImage;
/**
 * A `EmbeddingContent[]` is one input: its parts are joined with no separator and give one embedding. Use `EmbeddingContent[][]` to get one embedding per entry.
 */
type EmbeddingInput = string | number[] | string[] | number[][] | EmbeddingContent[] | EmbeddingContent[][];
interface EmbeddingCreateParams {
    input: EmbeddingInput;
    model?: string;
    encoding_format?: 'float' | 'base64';
    /**
     * Embedding normalization mode: -1 = none, 0 = max absolute int16, 1 = taxicab, 2 = Euclidean/L2 (default), >2 = p-norm.
     */
    embd_normalize?: number;
}
interface Embedding {
    object: 'embedding';
    index: number;
    embedding: number[] | string;
}
interface EmbeddingUsage {
    prompt_tokens: number;
    total_tokens: number;
}
interface CreateEmbeddingResponse {
    object: 'list';
    data: Embedding[];
    model: string;
    usage: EmbeddingUsage;
}
interface RerankParams {
    query: string;
    documents: string[];
    top_n?: number;
}
interface RerankResult {
    index: number;
    relevance_score: number;
}
interface RerankUsage {
    prompt_tokens: number;
    total_tokens: number;
}
interface RerankResponse {
    model: string;
    object: 'list';
    usage: RerankUsage;
    results: RerankResult[];
}
type SystemOneQuestion = {
    type: 'choice';
    instructions: any;
    criteria: Record<string, any>;
} | {
    type: 'score';
    instructions: any;
    criteria: any[];
} | {
    type: 'noul';
    instructions: any;
    criteria?: {
        true?: any;
        false?: any;
    };
};
interface SystemOneParams {
    state: any;
    questions: Record<string, SystemOneQuestion>;
}
type SystemOneAnswer = {
    type: 'choice';
    choice: string;
    probabilities: Record<string, number>;
    confidence: number;
} | {
    type: 'score';
    score: number;
    legend: Record<string, any>;
    probabilities: Record<string, number>;
    confidence: number;
} | {
    type: 'noul';
    noul: number;
};
interface SystemOneResponse {
    model: string;
    answers: Record<string, SystemOneAnswer>;
    usage: {
        input_tokens: number;
        output_tokens: number;
    };
}
interface SpeechParams {
    input: string;
    voice?: string;
    lang?: string;
    speaker_ref?: ArrayBuffer;
    response_format?: 'wav' | 'pcm';
    n_predict?: number;
    top_k?: number;
    top_p?: number;
    seed?: number;
    abortSignal?: AbortSignal;
}
interface SpeechResponse {
    audio: ArrayBuffer;
    sample_rate: number;
}
interface SpeechChunk extends SpeechResponse {
    final: boolean;
}
interface TranscriptionParams {
    file: ArrayBuffer;
    language?: string;
    prompt?: string;
    temperature?: number;
    max_tokens?: number;
    seed?: number;
    stream?: boolean;
    abortSignal?: AbortSignal;
}
interface TranscriptionUsage {
    type: 'tokens';
    input_tokens: number;
    output_tokens: number;
    total_tokens: number;
}
interface TranscriptionResponse {
    type: 'transcript.text.done';
    text: string;
    usage: TranscriptionUsage;
}
interface TranscriptionDelta {
    type: 'transcript.text.delta';
    delta: string;
}
type TranscriptionChunk = TranscriptionDelta | TranscriptionResponse;
interface RealtimeSessionParams {
    intent?: 'transcription';
    seed?: number;
}
interface RealtimeEvent {
    type: string;
    [key: string]: any;
}

interface HuggingFaceParams {
    /**
     * The repo name, e.g. user/model
     */
    repo: string;
    /**
     * The file name or path to file in the repo. Only file or quant is needed.
     */
    file?: string;
    /**
     * The GGUF quantization name, e.g. Q4_K_M, Q8_0, etc. Only file or quant is needed.
     *
     * By default, Q4_K_M will be used, then fallback to Q8_0, and finally the non-quantized version if no quantized version is found.
     */
    quant?: string;
    /**
     * The file name or path to file in the repo for mmproj. Only mmprojFile or mmprojQuant is needed.
     */
    mmprojFile?: string;
    /**
     * The GGUF quantization name for mmproj, e.g. Q4_K_M, Q8_0, etc. Only mmprojFile or mmprojQuant is needed.
     */
    mmprojQuant?: string;
    /**
     * The Hugging Face token with permission to access the repo. It can be omitted if the repo is public.
     */
    hfToken?: string;
}
declare function getHFModelSource(config: HuggingFaceParams): Promise<ModelSource>;
declare function getHFFileSHA256(url: string, headers: Record<string, string>): Promise<string | undefined>;

interface WllamaLogger {
    debug: typeof console.debug;
    log: typeof console.log;
    warn: typeof console.warn;
    error: typeof console.error;
}
interface WllamaConfig {
    /**
     * If true, suppress all log messages from native CPP code
     */
    suppressNativeLog?: boolean;
    /**
     * Custom logger functions
     */
    logger?: WllamaLogger;
    /**
     * Maximum number of parallel files to be downloaded
     *
     * Default: parallelDownloads = 3
     */
    parallelDownloads?: number;
    /**
     * Allow offline mode. If true, the model will be loaded from cache if it's available.
     *
     * Default: allowOffline = false
     */
    allowOffline?: boolean;
    /**
     * Custom cache manager (only for advanced usage)
     */
    cacheManager?: CacheManager;
    /**
     * Custom model manager (only for advanced usage)
     */
    modelManager?: ModelManager;
    /**
     * If true, allow a wasm file built from a different llama.cpp version
     *
     * Default: skipVersionCheck = false
     */
    skipVersionCheck?: boolean;
}
interface WllamaChatMessage {
    role: 'system' | 'user' | 'assistant';
    content: string;
}
interface AssetsPathConfig {
    default: string;
    'single-thread/wllama.wasm'?: string;
    'multi-thread/wllama.wasm'?: string;
}
interface ModelMetadata {
    hparams: {
        nVocab: number;
        nCtxTrain: number;
        nEmbd: number;
        nLayer: number;
    };
    meta: Record<string, string>;
}
/**
 * Logger preset with debug messages suppressed
 */
declare const LoggerWithoutDebug: {
    debug: () => void;
    assert(condition?: boolean, ...data: any[]): void;
    assert(value: any, message?: string, ...optionalParams: any[]): void;
    clear(): void;
    clear(): void;
    count(label?: string): void;
    count(label?: string): void;
    countReset(label?: string): void;
    countReset(label?: string): void;
    dir(item?: any, options?: any): void;
    dir(obj: any, options?: util.InspectOptions): void;
    dirxml(...data: any[]): void;
    dirxml(...data: any[]): void;
    error(...data: any[]): void;
    error(message?: any, ...optionalParams: any[]): void;
    group(...data: any[]): void;
    group(...label: any[]): void;
    groupCollapsed(...data: any[]): void;
    groupCollapsed(...label: any[]): void;
    groupEnd(): void;
    groupEnd(): void;
    info(...data: any[]): void;
    info(message?: any, ...optionalParams: any[]): void;
    log(...data: any[]): void;
    log(message?: any, ...optionalParams: any[]): void;
    table(tabularData?: any, properties?: string[]): void;
    table(tabularData: any, properties?: readonly string[]): void;
    time(label?: string): void;
    time(label?: string): void;
    timeEnd(label?: string): void;
    timeEnd(label?: string): void;
    timeLog(label?: string, ...data: any[]): void;
    timeLog(label?: string, ...data: any[]): void;
    timeStamp(label?: string): void;
    timeStamp(label?: string): void;
    trace(...data: any[]): void;
    trace(message?: any, ...optionalParams: any[]): void;
    warn(...data: any[]): void;
    warn(message?: any, ...optionalParams: any[]): void;
    Console: console.ConsoleConstructor;
    profile(label?: string): void;
    profileEnd(label?: string): void;
};
type WllamaErrorType = 'model_not_loaded' | 'download_error' | 'load_error' | 'version_mismatch' | 'kv_cache_full' | 'unknown_error' | 'inference_error';
declare class WllamaError extends Error {
    type: WllamaErrorType;
    constructor(message: string, type?: WllamaErrorType);
}
/**
 * AbortError is thrown when the user wants to abort the current operation.
 * This is equivalent to AbortError in Fetch API.
 */
declare class WllamaAbortError extends Error {
    name: string;
    constructor();
}
/**
 * RuntimeError is thrown when there is an error in the WASM runtime, such as stack overflow, OOM, etc.
 * Stack trace of the error in the WASM runtime can be included in the error object for debugging purpose.
 */
declare class WllamaRuntimeError extends Error {
    name: string;
    stack: string;
    constructor(message: string, stack: string);
}
/**
 * Set compatibility options for Wllama.
 * By default, these are set to URL of the latest builds on CDN, which requires internet to download. If you want to use local assets or have your own CDN, follow the instruction from @wllama/wllama-compat package.
 */
interface WllamaCompat {
    worker: string | {
        code: string;
    };
    wasm: string;
}
/**
 * Encode PCM samples to the base64 PCM16 format of `input_audio_buffer.append`.
 */
declare const encodeRealtimeAudio: (samples: Float32Array) => string;
/**
 * Decode the base64 PCM16 audio of a `response.output_audio.delta` event.
 */
declare const decodeRealtimeAudio: (delta: string) => Float32Array;
/**
 * A realtime audio session. Events follow the OAI realtime schema, same as the `/v1/realtime` route of llama-server.
 * Inference runs while `events()` is being read, so keep reading it to get a response.
 */
declare class WllamaRealtimeSession {
    private proxy;
    private reqId;
    private closed;
    private pending;
    private wakeUp;
    private sent;
    constructor(proxy: ProxyToWorker, reqId: number);
    /**
     * Send a client event, for example `session.update` or `response.create`.
     */
    send(event: RealtimeEvent): Promise<void>;
    /**
     * Same as sending `input_audio_buffer.append`.
     * @param samples mono PCM at `LoadedContextInfo.audio_input_rate`
     */
    appendAudio(samples: Float32Array): Promise<void>;
    /**
     * Read the server events. It ends when the session ends. Only one reader at a time.
     */
    events(): AsyncGenerator<RealtimeEvent>;
    /**
     * End the session and free its slot.
     */
    close(): Promise<void>;
}
declare class Wllama {
    cacheManager: CacheManager;
    modelManager: ModelManager;
    private compat;
    private proxy;
    private config;
    private pathConfig;
    private useMultiThread;
    private nbThreads;
    private useEmbeddings;
    private useRerank;
    private loadedContextInfo;
    private seed;
    private bosToken;
    private eosToken;
    private eotToken;
    private eogTokens;
    private addBosToken;
    private addEosToken;
    private mediaMarker?;
    private chatTemplate?;
    private metadata?;
    private hasEncoder;
    private decoderStartToken;
    private chatTemplateKwargs;
    constructor(pathConfig: AssetsPathConfig, wllamaConfig?: WllamaConfig);
    private logger;
    private checkModelLoaded;
    /**
     * Get the libllama version string, e.g. "b6327-4d74393".
     *
     * @returns version string embedded at build time.
     */
    static getLibllamaVersion(): string;
    /**
     * Set compatibility options for Wllama.
     * @param compat Set to null to disable compatibility, or 'default' to use the default compat resources from CDN.
     * @param mode 'safari' by default; If set to 'firefox_safari', the compat mode will **also** be enabled on Firefox, which will significantly degrade the performance but allow using WebGPU on Firefox.
     */
    setCompat(compat: WllamaCompat | null | 'default', mode?: 'safari' | 'firefox_safari'): void;
    /**
     * Check if the model is loaded via `loadModel()`
     */
    isModelLoaded(): boolean;
    /**
     * Get token ID associated to BOS (begin of sentence) token.
     *
     * NOTE: This can only being used after `loadModel` is called.
     *
     * @returns -1 if the model is not loaded.
     */
    getBOS(): number;
    /**
     * Get token ID associated to EOS (end of sentence) token.
     *
     * NOTE: This can only being used after `loadModel` is called.
     *
     * @returns -1 if the model is not loaded.
     */
    getEOS(): number;
    /**
     * Get token ID associated to EOT (end of turn) token.
     *
     * NOTE: This can only being used after `loadModel` is called.
     *
     * @returns -1 if the model is not loaded.
     */
    getEOT(): number;
    /**
     * Check if a given token is end-of-generation token (e.g. EOS, EOT, etc.)
     *
     * @param token the token ID to be checked
     * @returns true if the token is EOS, EOT, or any other end-of-generation tokens
     */
    isTokenEOG(token: number): boolean;
    /**
     * Get token ID associated to token used by decoder, to start generating output sequence(only usable for encoder-decoder architecture). In other words, encoder uses normal BOS and decoder uses this token.
     *
     * NOTE: This can only being used after `loadModel` is called.
     *
     * @returns -1 if the model is not loaded.
     */
    getDecoderStartToken(): number;
    /**
     * Get model hyper-parameters and metadata
     *
     * NOTE: This can only being used after `loadModel` is called.
     *
     * @returns ModelMetadata
     */
    getModelMetadata(): ModelMetadata;
    /**
     * Check if we're currently using multi-thread build.
     *
     * NOTE: This can only being used after `loadModel` is called.
     *
     * @returns true if multi-thread is used.
     */
    isMultithread(): boolean;
    /**
     * Get number of threads used in the current context.
     *
     * NOTE: This can only being used after `loadModel` is called.
     *
     * @returns number of threads
     */
    getNumThreads(): number;
    /**
     * Check if the current model uses encoder-decoder architecture
     *
     * NOTE: This can only being used after `loadModel` is called.
     *
     * @returns true if multi-thread is used.
     */
    isEncoderDecoderArchitecture(): boolean;
    /**
     * Must we add BOS token to the tokenized sequence?
     *
     * NOTE: This can only being used after `loadModel` is called.
     *
     * @returns true if BOS token must be added to the sequence
     */
    mustAddBosToken(): boolean;
    /**
     * Must we add EOS token to the tokenized sequence?
     *
     * NOTE: This can only being used after `loadModel` is called.
     *
     * @returns true if EOS token must be added to the sequence
     */
    mustAddEosToken(): boolean;
    /**
     * Get the jinja chat template comes with the model. It only available if the original model (before converting to gguf) has the template in `tokenizer_config.json`
     *
     * NOTE: This can only being used after `loadModel` is called.
     *
     * @returns the jinja template. null if there is no template in gguf
     */
    getChatTemplate(): string | null;
    /**
     * Check if WebGPU is supported by the current environment.
     * @returns true if WebGPU is supported
     */
    isSupportWebGPU(): boolean;
    /**
     * Load model from a given URL (or a list of URLs, in case the model is splitted into smaller files)
     * - If the model already been downloaded (via `downloadModel()`), then we will use the cached model
     * - Else, we download the model from internet
     * @param modelSourceOrURL
     * @param params
     */
    loadModelFromUrl(modelSourceOrURL: ModelSource | string, params?: LoadModelParams & DownloadOptions & {
        useCache?: boolean;
    }): Promise<void>;
    /**
     * Load model from a given Hugging Face model ID and file path.
     *
     * @param hfOptions
     * @param params
     */
    loadModelFromHF(hfOptions: HuggingFaceParams, params?: LoadModelParams & DownloadOptions & {
        useCache?: boolean;
    }): Promise<void>;
    /**
     * Load model from a given list of Blob.
     *
     * You can pass multiple buffers into the function (in case the model contains multiple shards).
     *
     * @param ggufBlobsOrModel Can be either list of Blobs (in case you use local file), or a Model object (in case you use ModelManager)
     * @param params LoadModelParams
     */
    loadModel(ggufBlobsOrModel: Blob[] | Model, params?: LoadModelParams): Promise<void>;
    getLoadedContextInfo(): LoadedContextInfo;
    /**
     * Calculate embedding vector for a given text or multimodal input.
     * By default, BOS and EOS tokens will be added automatically. You can use the "skipBOS" and "skipEOS" option to disable it.
     * @param options OAI-compatible embedding creation options
     * @returns OAI-compatible embedding response
     */
    createEmbedding(options: EmbeddingCreateParams): Promise<CreateEmbeddingResponse>;
    /**
     * Rerank a list of documents against a query.
     * Requires the model to be loaded with embeddings: true and pooling_type: 'rank'.
     * @param options Reranking options (query, documents, top_n)
     * @returns Reranking response with relevance scores sorted highest first
     */
    createRerank(options: RerankParams): Promise<RerankResponse>;
    /**
     * Answer typed questions about a state, using a decision model (TypeSafe-compatible System One API).
     * Image input is not supported yet.
     * @param options The state and the questions
     * @returns The answer of each question
     */
    createSystemOne(options: SystemOneParams): Promise<SystemOneResponse>;
    /**
     * Generate speech from text. The model must have audio output, see `LoadedContextInfo.has_audio_output`.
     * @param options speech options
     * @returns the whole audio (stream=false) or an async iterator of audio chunks (stream=true). A stream starts its request on the first read, so errors come from the iterator
     */
    createSpeech(options: SpeechParams & {
        stream?: false;
    }): Promise<SpeechResponse>;
    createSpeech(options: SpeechParams & {
        stream: true;
    }): Promise<AsyncIterable<SpeechChunk>>;
    /**
     * Transcribe an audio file. The model must have audio input, see `supportInputModality()`.
     * @param options OAI-compatible transcription options
     * @returns OAI-compatible transcription response (stream=false), void when done (stream=true + onData), or async iterator (stream=true, no onData)
     */
    createTranscription(options: TranscriptionParams & {
        stream?: false;
    }): Promise<TranscriptionResponse>;
    createTranscription(options: TranscriptionParams & StreamParams<TranscriptionChunk>): Promise<void>;
    createTranscription(options: TranscriptionParams & {
        stream: true;
    }): Promise<AsyncIterable<TranscriptionChunk>>;
    /**
     * Start a realtime audio session. The model must support it, see `LoadedContextInfo.has_audio_stream`.
     * It holds one slot until `close()` is called.
     * @param options session options
     * @returns the session
     */
    createRealtimeSession(options?: RealtimeSessionParams): Promise<WllamaRealtimeSession>;
    /**
     * Make chat completion for a given chat messages.
     * @param options OAI-compatible chat completion options
     * @returns OAI-compatible chat completion response (only the final result when stream=false) or an async iterator of completion chunks (when stream=true)
     */
    createChatCompletion(options: ChatCompletionParams & {
        stream?: false;
    }): Promise<ChatCompletionResponse>;
    createChatCompletion(options: ChatCompletionParams & StreamParams<ChatCompletionChunk>): Promise<void>;
    createChatCompletion(options: ChatCompletionParams & {
        stream: true;
    }): Promise<AsyncIterable<ChatCompletionChunk>>;
    /**
     * Make (raw) completion for a given text.
     * @param options OAI-compatible completion options
     * @returns OAI-compatible completion response (stream=false), void when done (stream=true + onData), or async iterator (stream=true, no onData)
     */
    createCompletion(options: RawCompletionParams & {
        stream?: false;
    }): Promise<RawCompletionResponse>;
    createCompletion(options: RawCompletionParams & StreamParams<RawCompletionChunk>): Promise<void>;
    createCompletion(options: RawCompletionParams & {
        stream: true;
    }): Promise<AsyncIterable<RawCompletionChunk>>;
    /**
     * Private implementation of createCompletion
     */
    private createCompletionImpl;
    /**
     * Same with `createCompletion`, but returns an async iterator instead.
     * Only called when stream=true and no onData is provided.
     */
    private createCompletionGenerator;
    /**
     * Whether the currently loaded model supports a specific input modality (e.g. image or audio).
     * @param modality
     * @returns
     */
    supportInputModality(modality: 'image' | 'audio'): boolean;
    /**
     * Unload the model and free all memory.
     *
     * Note: This function will NOT crash if model is not yet loaded
     */
    exit(): Promise<void>;
    /**
     * [FOR DEBUGGING ONLY] Run ggml backend ops tests without loading any model.
     *
     * Initializes the wasm runtime, executes `test-backend-ops` with the given args, then shuts down.
     *
     * For more info, please refer to guides/debug.md
     *
     * @param args Arguments forwarded to test-backend-ops (e.g. ["-o", "ADD"])
     * @returns retcode (0 = all tests passed) and success flag
     */
    testBackendOps(args?: string[]): Promise<{
        retcode: number;
        success: boolean;
    }>;
    /**
     * get debug info
     */
    _getDebugInfo(): Promise<any>;
    private jsonDecode;
    private replaceMedia;
    private prepareMultimodalInput;
    private prepareEmbeddingInput;
    private cancelRequest;
    private getSpeechChunks;
    private getSingleResult;
    private getResponse;
    private checkWasmVersion;
    getWorkerResources(): WllamaWorkerResources;
}

/**
 * Validates if a given string is a valid GGUF file path/URL
 * @param path The file path or URL to validate
 * @returns true if the path is a valid GGUF file path/URL
 */
declare const isValidGgufFile: (path: string) => boolean;

export { type AssetsPathConfig, type CacheEntry, type CacheEntryMetadata, CacheManager, type ChatCompletionAssistantMessage, type ChatCompletionChoice, type ChatCompletionChoiceLogprobs, type ChatCompletionChunk, type ChatCompletionChunkChoice, type ChatCompletionChunkDelta, type ChatCompletionLogprob, type ChatCompletionLogprobsContent, type ChatCompletionMessage, type ChatCompletionMessageAudio, type ChatCompletionMessageContent, type ChatCompletionMessageImage, type ChatCompletionMessageText, type ChatCompletionParams, type ChatCompletionResponse, type ChatCompletionSystemMessage, type ChatCompletionTool, type ChatCompletionToolCall, type ChatCompletionToolChoice, type ChatCompletionToolFunction, type ChatCompletionToolFunctionParameters, type ChatCompletionToolMessage, type ChatCompletionUsage, type ChatCompletionUserMessage, type ChatMessagePromptProgress, type CreateEmbeddingResponse, type DownloadOptions, type DownloadProgressCallback, type Embedding, type EmbeddingContent, type EmbeddingCreateParams, type EmbeddingInput, type EmbeddingUsage, type HuggingFaceParams, type LoadModelParams, type LoadedContextInfo, LogLevel, LoggerWithoutDebug, Model, ModelManager, type ModelManagerParams, type ModelMetadata, type ModelSource, ModelValidationStatus, POLYFILL_ETAG, type RawCompletionChoice, type RawCompletionChunk, type RawCompletionParams, type RawCompletionResponse, type RealtimeEvent, type RealtimeSessionParams, type RerankParams, type RerankResponse, type RerankResult, type RerankUsage, type ResultTimings, type SamplingParams, type SpeechChunk, type SpeechParams, type SpeechResponse, type StreamParams, type SystemOneAnswer, type SystemOneParams, type SystemOneQuestion, type SystemOneResponse, type TranscriptionChunk, type TranscriptionDelta, type TranscriptionParams, type TranscriptionResponse, type TranscriptionUsage, Wllama, WllamaAbortError, type WllamaChatMessage, type WllamaCompat, type WllamaConfig, WllamaError, type WllamaErrorType, type WllamaLogger, WllamaRealtimeSession, WllamaRuntimeError, decodeRealtimeAudio, encodeRealtimeAudio, getHFFileSHA256, getHFModelSource, isValidGgufFile };
