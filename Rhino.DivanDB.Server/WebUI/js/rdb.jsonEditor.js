﻿function InitializeJSONEditor(jsonToEdit) {
    $('#txtJSON').val(JSON.stringify(jsonToEdit));

    $('#editorTabs').tabs({
        select: function (event, ui) {
            if (ui.index == 1) { //raw 
                if ($('#editorTabs').find(".ui-tabs-panel:visible").attr('id') == 'fancyJSONEditor') {
                    var treeJSON = GetJSONFromTree();
                    $('#txtJSON').val(treeJSON);
                }
            } else if (ui.index == 0) { //edit
                var json = JSON.parse($('#txtJSON').val());
                LoadJSONToTree(json);
            } else if (ui.index == 2) { //view
                if ($('#editorTabs').find(".ui-tabs-panel:visible").attr('id') == 'fancyJSONEditor') {
                    var json = GetJSONFromTree();
                    $('#txtJSON').val(json);
                }
                LoadJSONView(JSON.parse($('#txtJSON').val()));
            }
        }
    });
    
    $('#editorApplyChanges, #editorApplyChanges2').button({
        icons: { primary: 'ui-icon-disk' }
    }).click(function () {
        UpdateSelectedNode(); 
    });
    
    var json = JSON.parse($('#txtJSON').val());
    LoadJSONToTree(json);
}

function GetJSONFromEditor() {
    if ($('#editorTabs').find(".ui-tabs-panel:visible").attr('id') == 'fancyJSONEditor') {
        UpdateSelectedNode(); 
        return GetJSONFromTree();        
    } else { //edit
        return $('#txtJSON').val();        
    } 
}

function LoadJSONView(json) {
    var view = $('<div class="jsonViewWrapper"></div>');
    $(view).html(JSONToViewHTML(json));
    $('#jsonViewer').html(view);

    $('#jsonViewer').find('.jsonObjectView:first').css('border', 'none').css('padding', '0').css('margin-left', '0');

    $('#jsonViewer').find('.arrayNameView').click(function () {
        $(this).next().slideToggle();
        var current = $(this).children('div').html();
        if (current == 'hide')
            $(this).children('div').html('show');
        else
            $(this).children('div').html('hide'); 
    }).hover(function () {
        var text;
        if ($(this).next().is(':visible')) {
            text = 'hide';
        } else {
            text = 'show';
        }
        $(this).append('<div style="position:absolute; right: 5px; top: 8px; font-size: 10px; color: #ccc;">' + text + '</div>');
    }, function () {
        $(this).children('div').remove();
    });

}

function LoadTree(json) {
    $('#jsonTree').tree({
        data: {
            type: 'json',
            opts: {
                static: json
            }
        },
        types: {
            "default": {
                clickable: true,
                renameable: true,
                deletable: true,
                creatable: true,
                draggable: true,
                max_children: -1,
                max_depth: -1,
                valid_children: "all",

                icon: {
                    image: false,
                    position: false
                }
            },
            "jsonValue": {
                valid_children: "none",
                max_children: 0,
                max_depth: 0,
                renameable: true,
                icon: {
                    image: 'js/jstree/themes/icons/txt.png'
                }
            }
        },
        callback: {
            onselect: function (node, tree) {
                if (tree.get_type(node) == 'jsonValue') {
                    $('#selectedJSONname').val($(node).children('a').text().trim());
                    $('#selectedJSONval').val(unescape($(node).attr('jsonvalue')));
                    $('#jsonArrayEditor').fadeOut('slow', function () {
                        $('#jsonEditor').fadeIn('slow');
                    });

                } else {
                    $('#selectedJSONArrayName').val($(node).children('a').text().trim());
                    $('#jsonEditor').fadeOut('slow', function () {
                        $('#jsonArrayEditor').fadeIn('slow');
                    });
                }
            },
            onload: function (tree) {
            }
        }
    });
    $('#jsonArrayEditor').hide();
    $('#jsonEditor').hide();    
    $.tree.focused().container.find('li:first').click();
    $.tree.focused().container.find('.leaf:first a:first').click();
}

function CreateArray() {
    if ($.tree.focused().get_type(node) == 'jsonValue') {
        var node = $.tree.focused().create({
            data: 'New Array',
            attributes: { rel: 'default' }
        }, $.tree.focused().selected, 'after');
        if (node) {
            $.tree.focused().select_branch(node);
        }                
    } else {
        var node = $.tree.focused().create({ data: 'New Array' });
        if (node) {
            $.tree.focused().select_branch(node);
        }
    }

}

function CreateValue() {
    if ($.tree.focused().get_type(node) == 'jsonValue') {
        var node = $.tree.focused().create({
            data: 'New Value',
            attributes: { rel: 'jsonValue', "jsonvalue": '' }
        }, $.tree.focused().selected, 'after');
        if (node) {
            $.tree.focused().select_branch(node);
        }  
    } else {
        var node = $.tree.focused().create({
            data: 'New Value',
            attributes: { rel: 'jsonValue', "jsonvalue": '' }
        });
        if (node) {
            $.tree.focused().select_branch(node);
        }
    }
}

function LoadJSONToTree(json) {
    json = { Document : json };
    json = JSONToTreeJSON(json, '');
    $('#parsedJSON').val(JSON.stringify(json, null, '\t'));
    LoadTree(json);
}

function GetJSONFromTree() {
    //tree.get only gives values for open elements, expand all
    $.tree.focused().open_all();
    var treeJSON = $.tree.focused().get(null, null, { outer_attrib: ["jsonvalue"] });
    var convertedTreeJSON = traverseTreeJSON(treeJSON);
    convertedTreeJSON = convertedTreeJSON.Document;
    var jsonString = JSON.stringify(convertedTreeJSON);
    return jsonString;
}


function traverseTreeJSON(json) {
    var retJSON = {};
    if (json.children) {
        var childrenJSON = {};
        $(json.children).each(function () {
            var childJSON = traverseTreeJSON(this);
            $.each(childJSON, function (key, value) {
                childrenJSON[key] = value;
            });
        });

        if (json.data.title && json.data.title != '') {
            retJSON[json.data.title] = childrenJSON;
        }
    } else {
        if (json.data.title && json.data.title != '') {
            retJSON[json.data.title] = unescape(json.attributes.jsonvalue);
        }
    }
    return retJSON;
}

function JSONToViewHTML(jsonObj) {
    if (typeof jsonObj == "object") {
        var jsonDiv = $('<div class="jsonObjectView"></div>');
        $.each(jsonObj, function (key, value) {
            if (value) {
                // key is either an array index or object key                    
                var children = JSONToViewHTML(value);

                if (typeof children == "object") {
                    $(jsonDiv).append('<span class="arrayNameView">' + key + '</span>');
                    $(jsonDiv).append(children);
                } else {
                        var childDiv = $('<div class="jsonObjectMemberView"></div>');
                        $(childDiv).append('<span class="memberNameView">' + key + '</span>');
                        $(childDiv).append('<span class="memberValueView">' +  children + '</span>');
                        $(jsonDiv).append(childDiv);
                }
            } else {
                var childDiv = $('<div class="jsonObjectMemberView"></div>');
                $(childDiv).append('<span class="memberNameView">' + key + '</span>');
                $(childDiv).append('<span class="memberValueView"></span>');
                $(jsonDiv).append(childDiv);
            }
        });
        debugger;
        return jsonDiv;
    }
    else {
        // jsonOb is a number or string
        debugger;
        return jsonObj;                
    }
}

function JSONToTreeJSON(jsonObj) {
    if (typeof jsonObj == "object") {
        var jsonArr = [];
        $.each(jsonObj, function (key, value) {
            if (value) {
                // key is either an array index or object key                    
                var children = JSONToTreeJSON(value);

                if (typeof children == "object") {
                    jsonArr.push({
                        data: key,
                        children: children
                    });
                } else {
                    jsonArr.push({
                        data: key,
                        attributes: { rel : 'jsonValue',  "jsonvalue": escape(children) }
                    });
                }
            } else {
                jsonArr.push({
                    data: key,
                    attributes: { rel : 'jsonValue', "jsonvalue": '' }
                });
            }
        });
        return jsonArr;
    }
    else {
        // jsonOb is a number or string
        return jsonObj;
    }
}

function UpdateSelectedNode() {
    if ($.tree.focused().get_type($.tree.focused().selected) == 'jsonValue') {
        $.tree.focused().rename($.tree.focused().selected, $('#selectedJSONname').val());
        $.tree.focused().selected.attr('jsonvalue', escape($('#selectedJSONval').val()));
    } else {
        $.tree.focused().rename($.tree.focused().selected, $('#selectedJSONArrayName').val());
    }
}