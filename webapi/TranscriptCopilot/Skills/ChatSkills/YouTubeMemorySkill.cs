﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Memory.AzureCognitiveSearchVector;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SkillDefinition;
using SemanticKernel.Service.CopilotChat.Options;
using SemanticKernel.Service.CopilotChat.Skills.SortSkill;

namespace SemanticKernel.Service.CopilotChat.Skills.ChatSkills
{
    public class YouTubeMemorySkill
    {
        private readonly PromptsOptions _promptOptions;
        private readonly YouTubeMemoryOptions _youTubeImportOptions;
        private readonly AzureSearchMemoryClient _azureCognitiveSearchMemory;

        public YouTubeMemorySkill(
            IOptions<PromptsOptions> promptOptions,
            IOptions<YouTubeMemoryOptions> youTubeImportOptions)
        {
            _promptOptions = promptOptions.Value;
            _youTubeImportOptions = youTubeImportOptions.Value;

            var acsEndpoint = Environment.GetEnvironmentVariable("ACS_INSTANCE");
            var acsApiKey = Environment.GetEnvironmentVariable("ACS_KEY");

            var searchEndpoint = $"https://{acsEndpoint}.search.windows.net/";


            HttpClient client = new HttpClient();
            _azureCognitiveSearchMemory = new AzureSearchMemoryClient(searchEndpoint, acsApiKey, client);
        }

        [SKFunction, Description("Query youtube video transcription in the memory given a user message")]
        [SKParameter("tokenLimit", "Maximum number of tokens")]
        public async Task<string> QueryYouTubeVideosAsync([Description("Query to match.")] string query, SKContext context, IKernel kernel, SortSkill.SortType sortType)
        {
            int tokenLimit = int.Parse(context.Variables["tokenLimit"], new NumberFormatInfo());
            var remainingToken = tokenLimit;

            var videoCollections = new[] { _youTubeImportOptions.GlobalDocumentCollectionName };
            var relevantMemories = await GetRelevantMemories(query, videoCollections, sortType);
            var videosText = BuildDocumentText(ref remainingToken, relevantMemories);

            return string.IsNullOrEmpty(videosText)
                ? string.Empty
                : $"Here are relevant YouTube snippets and IDs:\n{videosText}";
        }

        private async Task<List<MemoryQueryResult>> GetRelevantMemories(string query, string[] documentCollections, SortSkill.SortType sortType)
        {
            var relevantMemories = new List<MemoryQueryResult>();

            foreach (var documentCollection in documentCollections)
            {
                var results = _azureCognitiveSearchMemory.SearchAsync(
                    documentCollection,
                    query,
                    sortType
                    );

                await foreach (var memory in results)
                {
                    relevantMemories.Add(memory);
                }
            }

            return relevantMemories.OrderByDescending(m => m.Relevance).ToList();
        }

        private static string BuildDocumentText(ref int remainingToken, List<MemoryQueryResult> relevantMemories)
        {
            var documentsText = string.Empty;

            foreach (var memory in relevantMemories)
            {
                var tokenCount = Utilities.TokenCount(memory.Metadata.Text);

                if (remainingToken - tokenCount <= 0)
                {
                    break;
                }

                documentsText += $"\n\nTranscript from YouTube ID: {memory.Metadata.Id}: {memory.Metadata.Text}";
                remainingToken -= tokenCount;
            }

            return documentsText;
        }
    }
}
