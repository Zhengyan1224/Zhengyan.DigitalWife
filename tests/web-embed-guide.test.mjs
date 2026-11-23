import assert from 'node:assert/strict';
import test from 'node:test';
import {buildEmbedExamples} from '../src/Zhengyan.DigitalWife.GamePlayer.Web/wwwroot/embed-guide.js';

const options = {playerUrl:'https://games.example.com:8443/index.html?project=demo&label=%22test%22',
    businessUrl:'https://portal.example.com:9443/app/',width:960,height:540};

test('game snippets retain URL parameters and ports, and put embedding permissions on the correct origins',()=>{
    const result=buildEmbedExamples(options);
    assert.match(result.html,/<iframe/);
    assert.match(result.html,/embed=1/);
    assert.match(result.html,/project=demo&amp;label=%22test%22&amp;embed=1/);
    assert.match(result.html,/allow="autoplay; microphone; fullscreen"/);
    assert.match(result.html,/aspect-ratio:960\/540/);
    assert.match(result.playerHeaders,/frame-ancestors 'self' https:\/\/portal.example.com:9443;/);
    assert.match(result.businessHeaders,/frame-src 'self' https:\/\/games.example.com:8443;/);
    assert.match(result.resourceHeaders,/Access-Control-Allow-Origin: https:\/\/games.example.com:8443/);
    assert.doesNotMatch(result.resourceHeaders,/portal.example.com/);
});

test('sprite snippets use the classic host script and the player entry, not the preview page',()=>{
    const result=buildEmbedExamples({...options,mode:'sprite',width:500,height:720});
    assert.match(result.html,/<script defer/);
    assert.match(result.html,/src="https:\/\/games.example.com:8443\/desktop-sprite.js"/);
    assert.match(result.html,/data-src="https:\/\/games.example.com:8443\/index.html\?/);
    assert.match(result.html,/data-width="500"/);
    assert.doesNotMatch(result.html,/type="module"|desktop-sprite.html/);
    assert.match(result.businessHeaders,/script-src 'self' https:\/\/games.example.com:8443;/);
});

test('unsafe URLs and invalid CSS dimensions cannot be copied as executable embedding code',()=>{
    for(const playerUrl of ['javascript:alert(1)','data:text/html,test','https://user:password@games.example.com/','/relative'])
        assert.throws(()=>buildEmbedExamples({...options,playerUrl}));
    for(const width of [0,-1,8193,NaN,'960px','1;position:fixed',1.5])
        assert.throws(()=>buildEmbedExamples({...options,width}));
    const result=buildEmbedExamples({...options,playerUrl:'https://games.example.com/?text=<iframe>&quote="'});
    assert.doesNotMatch(result.html,/text=<iframe>/);
    assert.match(result.html,/&amp;quote=/);
});
