/// <reference path="../../typings/tsd.d.ts" />

import getIndexTermsCommand = require("commands/database/index/getIndexTermsCommand");
import getDocumentsMetadataByIDPrefixCommand = require("commands/database/documents/getDocumentsMetadataByIDPrefixCommand");
import database = require("models/resources/database");
import getIndexEntriesFieldsCommand = require("commands/database/index/getIndexEntriesFieldsCommand");
import collection = require("models/database/documents/collection");
import document = require("models/database/documents/document");

class queryUtil {

    static readonly AutoPrefix = "auto/";
    static readonly DynamicPrefix = "collection/";
    static readonly AllDocs = "AllDocs";

    /**
     * Escapes lucene single term
     * 
     * Note: Do not use this method for escaping entire query unless you want to end up with: query\:value\ AND\ a\:b
     * @param query query to escape
     */
    static escapeTerm(term: string) {
        var output = "";

        for (var i = 0; i < term.length; i++) {
            var c = term.charAt(i);
            if (c === '\\' || c === '+' || c === '-' || c === '!' || c === '(' || c === ')'
                || c === ':' || c === '^' || c === '[' || c === ']' || c === '\"'
                || c === '{' || c === '}' || c === '~' || c === '*' || c === '?'
                || c === '|' || c === '&' || c === ' ') {
                output += "\\";
            }
            output += c;
        }

        return output;
    }

    static fetchIndexFields(db: database, indexName: string, outputFields: KnockoutObservableArray<string>): void {
        outputFields([]);

        // Fetch the index definition so that we get an updated list of fields to be used as sort by options.
        // Fields don't show for All Documents.
        const isAllDocumentsDynamicQuery = indexName === this.AllDocs;
        if (!isAllDocumentsDynamicQuery) {

            //if index is not dynamic, get columns using index definition, else get it using first index result
            if (indexName.startsWith(queryUtil.DynamicPrefix)) {
                new collection(indexName.substr(queryUtil.DynamicPrefix.length), db)
                    .fetchDocuments(0, 1)
                    .done(result => {
                        if (result && result.items.length > 0) {
                            const propertyNames = new document(result.items[0]).getDocumentPropertyNames();
                            outputFields(propertyNames);
                        }
                    });
            } else {
                new getIndexEntriesFieldsCommand(indexName, db)
                    .execute()
                    .done((fields) => {
                        //TODO: self.isTestIndex(result.IsTestIndex);
                        outputFields(fields.Results);
                    });
            }
        }
    }

    /*static queryCompleter(indexFields: KnockoutObservableArray<string>,
        activeDatabase: KnockoutObservable<database>,
        editor: any,
        session: any,
        pos: AceAjax.Position,
        prefix: string,
        callback: (errors: any[], worldlist: { name: string; value: string; score: number; meta: string }[]) => void,
        collections: KnockoutObservableArray<collection>,
        indexes: KnockoutObservableArray<Raven.Client.Documents.Operations.IndexInformation>) {

        let currentToken: AceAjax.TokenInfo = session.getTokenAt(pos.row, pos.column);
        // If token is space, use the previous token
        if (currentToken && currentToken.start > 0 && /^\s+$/g.test(currentToken.value)) {
            currentToken = session.getTokenAt(pos.row, currentToken.start - 1);
        }

        let identifier: string;
        let tokensAfterKeyword: AceAjax.TokenInfo[] = [];
        const getLastKeyword = () => {
            let operator: string;
            for (let row = 0; row <= pos.row; row++) {
                let lineTokens: AceAjax.TokenInfo[] = session.getTokens(pos.row - row);

                for (let i = lineTokens.length - 1; i >= 0; i--) {
                    const token = lineTokens[i];
                    switch (token.type) {
                    case "keyword":
                        const keyword = token.value.toLowerCase();
                        if (keyword === "desc" ||
                            keyword === "asc" ||
                            keyword === "and" ||
                            keyword === "or")
                            continue;

                        if (operator)
                            return keyword + operator;
                        return keyword;

                    case "keyword.operator":
                        operator = token.value;
                        break;
                    case "identifier":
                        identifier = token.value;
                        break;
                    default:
                        tokensAfterKeyword.push(token)
                        break;
                    }
                }
            }
        }

        const getIndexName: () => [string, boolean] = () => {
            let indexName: string;

            for (let row = 0; row <= pos.row; row++) {
                let lineTokens: AceAjax.TokenInfo[] = session.getTokens(pos.row - row);

                for (let i = lineTokens.length - 1; i >= 0; i--) {
                    const token = lineTokens[i];
                    switch (token.type) {
                    case "keyword":
                        const keyword = token.value.toLowerCase();
                        if (keyword === "from")
                            return [indexName, false];
                        if (keyword === "index")
                            return [indexName, true];
                        break;

                    case "identifier":
                        indexName = token.value;
                        break;
                    }
                }
            }
        }

        const lastKeyword = getLastKeyword();
        if (!lastKeyword)
            return;

        const hasStringToken = () => {
            for (var i = tokensAfterKeyword.length - 1; i >= 0; i--) {
                const token = tokensAfterKeyword[i];
                if (token.type === "string") {
                    return true;
                }
            }
        }

        //if (!currentToken || typeof currentToken.type === "string") {
        //   if (currentToken.type === "keyword") {
        switch (lastKeyword) {
        case "from":
        {
            /!* if (hasStringToken())
                 return;*!/

            callback(null,
                [{ name: "@all_docs", value: "@all_docs", score: 1000, meta: "all collections" }]);
            callback(null,
                collections().map(collection => {
                    const documentCount = collection.documentCount();
                    return {
                        name: collection.name,
                        value: collection.name,
                        score: documentCount + 10,
                        meta: "collection"
                    };
                }));
            break;
        }
        case "index":
        {
            /!* if (hasStringToken())
                 return;*!/
            callback(null,
                indexes().map(index => {
                    return { name: index.Name, value: `'${index.Name}'`, score: 10, meta: "index" };
                }));
            break;
        }
        case "select":
        case "by": // group by, order by
        case "where":
        {
            callback(null,
                indexFields().map(field => {
                    return { name: field, value: field, score: 10, meta: "field" };
                }));
            break;
            }
        case "where=":
        {
            // first, calculate and validate the column name
            let currentField = identifier;
            if (!currentField || !indexFields().find(x => x === currentField))
                return;

            let currentValue: string = "";

                /!*currentValue = currentToken.value.trim();
                const rowTokens: any[] = session.getTokens(pos.row);
                if (!!rowTokens && rowTokens.length > 1) {
                    currentColumnName = rowTokens[rowTokens.length - 2].value.trim();
                    currentColumnName = currentColumnName.substring(0, currentColumnName.length - 1);
                }*!/

            // for non dynamic indexes query index terms, for dynamic indexes, try perform general auto complete
            const [indexName, isStaticIndex] = getIndexName();
            if (!indexName)
                return; // todo: try to callback with error
                    
            if (isStaticIndex) {
                new getIndexTermsCommand(indexName, field, activeDatabase(), 20)
                    .execute()
                    .done(terms => {
                        if (terms && terms.Terms.length > 0) {
                            callback(null,
                                terms.Terms.map(curVal => {
                                    return { name: curVal, value: curVal, score: 10, meta: "value" };
                                }));
                        }
                    });
            } else {
                if (currentValue.length > 0) {
                    new getDocumentsMetadataByIDPrefixCommand(currentValue, 10, activeDatabase())
                        .execute()
                        .done((results: metadataAwareDto[]) => {
                            if (results && results.length > 0) {
                                callback(null,
                                    results.map(curVal => {
                                        return { name: curVal["@metadata"]["@id"], value: curVal["@metadata"]["@id"], score: 10, meta: "value" };
                                    }));
                            }
                        });
                } else {
                    callback([{ error: "notext" }], null);
                }
            }



            callback(null,
                indexFields().map(field => {
                    return { name: field, value: field, score: 10, meta: "field" };
                }));
            break;
        }
        default:
        {
            break;
        }
        }
    }*/

    static formatIndexQuery(indexName: string, ...predicates: { name?: string, value?: string }[]) {
        let query = `from index '${indexName}'`;
        if (predicates && predicates.length) {
            query = predicates.reduce((result, field) => {
                return `${result} where ${field.name} = '${field.value}'`;
            }, query);
        }

        return query;
    }
}

export = queryUtil;
