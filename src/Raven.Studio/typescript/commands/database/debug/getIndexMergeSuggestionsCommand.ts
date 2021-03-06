import commandBase = require("commands/commandBase");
import database = require("models/resources/database");

class getIndexMergeSuggestionsCommand extends commandBase {

    constructor(private db: database) {
        super();
    }

    execute(): JQueryPromise<any> { //TODO: replace with type
        var url = "/debug/suggest-index-merge";//TODO: use endpoints
        return this.query(url, null, this.db);
    }
} 

export = getIndexMergeSuggestionsCommand;
